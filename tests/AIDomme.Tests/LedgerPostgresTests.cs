using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AIDomme.Data;
using AIDomme.Persistence;
using AIDomme.Sessions;
using AIDomme.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIDomme.Tests;

public sealed partial class TrackerPostgresTests
{
    private const string Api = "/api/ext/aidome";

    private static async Task<JsonElement> RecordOrgasmAsync(HttpClient client, int? sessionId = null)
    {
        var tracker = await JsonAsync(await client.PostAsJsonAsync($"{Api}/tracker/initialize", new { }));
        var body = new RecordTrackerEvent(Guid.NewGuid(), tracker.GetProperty("settingId").GetGuid(), "orgasm", DateTime.UtcNow, "button")
        {
            GuidedSessionId = sessionId,
        };
        return await JsonAsync(await client.PostAsJsonAsync($"{Api}/tracker/events", body));
    }

    private static async Task<JsonElement> ProposeAsync(HttpClient client, CommitmentTerms terms)
        => await JsonAsync(await client.PostAsJsonAsync($"{Api}/commitments/propose", new CommitmentProposal(terms)));

    private static JsonElement Pending(JsonElement ledger)
        => Assert.Single(ledger.GetProperty("commitments").EnumerateArray(), x => x.GetProperty("status").GetString() == "Pending");

    [PostgresTrackerFact]
    public async Task Promises_AreAnswered_FoldIntoTheNextSession_AndAreKeptWhenSheEndsIt()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync();
        using var client = Client(app); using var other = Client(app, 2);
        var proposed = Pending(await ProposeAsync(client, new() { NoOrgasm = true }));
        Assert.NotEmpty(proposed.GetProperty("stakes").EnumerateArray());
        var id = proposed.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsync($"{Api}/commitments/{id}/accept", null)).StatusCode);
        var accepted = await JsonAsync(await client.PostAsync($"{Api}/commitments/{id}/accept", null));
        Assert.Equal("Active", accepted.GetProperty("commitments")[0].GetProperty("status").GetString());

        var started = await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/start", StartBody()));
        var session = started.GetProperty("session");
        var owed = session.GetProperty("contract").GetProperty("obligations");
        Assert.Equal(id, Assert.Single(owed.GetProperty("commitments").EnumerateArray()).GetGuid());
        Assert.NotNull(session.GetProperty("noOrgasmUntilUtc").Deserialize<DateTime?>());
        Assert.Contains(started.GetProperty("messages").EnumerateArray(), x => x.GetProperty("content").GetString()!.Contains("promise"));
        // Promises are made before a session, not during one.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/commitments/propose", new CommitmentProposal(new() { Long = true }))).StatusCode);

        var (sessionId, step) = (session.GetProperty("sessionId").GetInt32(), session.GetProperty("stepNumber").GetInt32());
        await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/control", new SessionControlRequest(Guid.NewGuid(), sessionId, step, "request-permission")));
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        await using (var db = runtime.Factory.Open()) Assert.False((await db.CumRequests.SingleAsync()).Decision);

        // She ends it herself: the promise for this session is kept.
        runtime.Session.State!.FinishDecided = true;
        await runtime.Session.EndAsync(SessionOutcome.Denied, default);
        var ledger = await JsonAsync(await client.GetAsync($"{Api}/ledger"));
        Assert.Equal("Kept", ledger.GetProperty("commitments")[0].GetProperty("status").GetString());
        Assert.Empty((await JsonAsync(await other.GetAsync($"{Api}/ledger"))).GetProperty("commitments").EnumerateArray());
    }

    [PostgresTrackerFact]
    public async Task ABrokenPromise_OutsideASession_IsAskedAbout_AndTheAnswerWritesTheStakesToTheLedger()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync(); using var client = Client(app);
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        var id = Pending(await ProposeAsync(client, new() { Scope = CommitmentScope.Days, Days = 3, NoOrgasm = true })).GetProperty("id").GetGuid();
        await JsonAsync(await client.PostAsync($"{Api}/commitments/{id}/accept", null));

        await RecordOrgasmAsync(client);
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        var question = Assert.Single((await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("followUps").EnumerateArray());
        Assert.Equal("UnauthorizedOrgasm", question.GetProperty("kind").GetString());
        Assert.Equal(2, question.GetProperty("answers").GetArrayLength());
        // Nothing else happens until she has an answer.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"{Api}/session/start", StartBody())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"{Api}/followups/{question.GetProperty("id").GetGuid()}/answer", new FollowUpAnswerRequest("maybe"))).StatusCode);
        var answered = await JsonAsync(await client.PostAsJsonAsync($"{Api}/followups/{question.GetProperty("id").GetGuid()}/answer", new FollowUpAnswerRequest("intent")));
        Assert.Empty(answered.GetProperty("followUps").EnumerateArray());
        Assert.Contains(answered.GetProperty("messages").EnumerateArray(), x => x.GetProperty("content").GetString()!.Contains("on purpose", StringComparison.OrdinalIgnoreCase)
            && x.GetProperty("content").GetString()!.Contains("You owe me"));

        var ledger = await JsonAsync(await client.GetAsync($"{Api}/ledger"));
        Assert.Equal("Broken", ledger.GetProperty("commitments")[0].GetProperty("status").GetString());
        var entries = ledger.GetProperty("entries").EnumerateArray().ToList();
        Assert.All(entries, x => Assert.Equal("Commitment", x.GetProperty("origin").GetString()));
        Assert.Contains(entries, x => x.GetProperty("kind").GetString() == "Denial");
        Assert.Contains(entries, x => x.GetProperty("kind").GetString() == "PunishmentSession");

        // An orgasm undone in the tracker is not held against anyone.
        var recorded = await RecordOrgasmAsync(client);
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        Assert.Single((await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("followUps").EnumerateArray());
        await JsonAsync(await client.PostAsync($"{Api}/tracker/events/{recorded.GetProperty("event").GetProperty("id").GetGuid()}/undo", null));
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        Assert.Empty((await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("followUps").EnumerateArray());

        // What is owed is folded into the next session: it is a punishment session, and she denies.
        var session = (await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/start", StartBody()))).GetProperty("session");
        Assert.True(session.GetProperty("contract").GetProperty("punishment").GetBoolean());
        Assert.NotNull(session.GetProperty("noOrgasmUntilUtc").Deserialize<DateTime?>());
        runtime.Session.State!.FinishDecided = true;
        await runtime.Session.EndAsync(SessionOutcome.Denied, default);
        await using var db = runtime.Factory.Open();
        var served = await db.Ledger.SingleAsync(x => x.Kind == LedgerKind.PunishmentSession);
        Assert.Equal((LedgerStatus.Served, 0), (served.Status, served.Remaining));
        Assert.Equal(session.GetProperty("sessionId").GetInt32(), served.ServingSessionId);
    }

    [PostgresTrackerFact]
    public async Task AnInterruptedSession_IsAskedAbout_BeforeItCanResume_AndWalkingOutCosts()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync(); using var client = Client(app);
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        var started = (await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/start", StartBody()))).GetProperty("session");
        var (sessionId, step) = (started.GetProperty("sessionId").GetInt32(), started.GetProperty("stepNumber").GetInt32());

        // Cove stops mid-session.
        await runtime.Session.InterruptAsync(default);
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        var state = await JsonAsync(await client.GetAsync($"{Api}/state"));
        Assert.True(state.GetProperty("session").GetProperty("interrupted").GetBoolean());
        var question = Assert.Single(state.GetProperty("followUps").EnumerateArray());
        Assert.Equal("InterruptedSession", question.GetProperty("kind").GetString());
        var resumed = await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/control", new SessionControlRequest(Guid.NewGuid(), sessionId, step, "resume")));
        Assert.True(resumed.GetProperty("session").GetProperty("isPaused").GetBoolean());

        var answered = await JsonAsync(await client.PostAsJsonAsync($"{Api}/followups/{question.GetProperty("id").GetGuid()}/answer", new FollowUpAnswerRequest("quit")));
        Assert.False(answered.GetProperty("sessionActive").GetBoolean());
        await using var db = runtime.Factory.Open();
        Assert.Equal(SessionOutcome.Abandoned, (await db.Sessions.SingleAsync(x => x.Id == sessionId)).Outcome);
        var owed = await db.Ledger.Where(x => x.FollowUpId == question.GetProperty("id").GetGuid()).ToListAsync();
        Assert.Contains(owed, x => x.Kind == LedgerKind.PunishmentSession && x.Origin == LedgerOrigin.Rule);
        Assert.Contains(owed, x => x.Kind == LedgerKind.Edges);
    }

    [PostgresTrackerFact]
    public async Task AnOrgasmWithoutPermission_InASession_IsAskedAbout_AndAnAcceptedOfferIsRecordedWithItsPrice()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync(); using var client = Client(app);
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        var started = (await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/start", StartBody(SessionMode.FreePlay)))).GetProperty("session");
        var sessionId = started.GetProperty("sessionId").GetInt32();
        Assert.Equal(10, runtime.Session.State!.OfferTemplates.Count);

        // A deal on the table, as she would make it: accepting records the price in the ledger.
        var s = runtime.Session.State!;
        s.OfferTemplates = s.OfferTemplates.Where(x => x.Kind == OfferKind.PunishmentSession).ToList();
        var offer = Offers.Make(s, OfferBuys.Orgasm, new FixedRandom(0.5), DateTime.UtcNow)!;
        await using (var db = runtime.Factory.Open())
        {
            db.Add(new OfferRecord { Id = offer.Id, SessionId = sessionId, Buys = offer.Buys, Kind = offer.Kind, Amount = offer.Amount, Severity = offer.Severity,
                Points = offer.Points, Flavor = offer.Flavor, Text = offer.Text, Status = OfferStatus.Proposed, CreatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        s.Offer = offer;
        var view = (await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("session");
        Assert.Equal(offer.Text, view.GetProperty("offer").GetProperty("text").GetString());
        var accepted = (await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/control",
            new SessionControlRequest(Guid.NewGuid(), sessionId, view.GetProperty("stepNumber").GetInt32(), "offer-accept")))).GetProperty("session");
        Assert.True(accepted.GetProperty("permissionGranted").GetBoolean());
        await using (var db = runtime.Factory.Open())
        {
            Assert.Equal(OfferStatus.Accepted, (await db.Offers.SingleAsync()).Status);
            var price = await db.Ledger.SingleAsync();
            Assert.Equal((LedgerKind.PunishmentSession, LedgerOrigin.Offer, offer.Id), (price.Kind, price.Origin, price.OfferId!.Value));
        }

        // She granted it, then took it back: an orgasm now is without permission.
        runtime.Session.State!.PermissionGranted = false;
        await RecordOrgasmAsync(client, sessionId);
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        var question = Assert.Single((await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("followUps").EnumerateArray());
        Assert.Equal(sessionId, question.GetProperty("sessionId").GetInt32());
        await JsonAsync(await client.PostAsJsonAsync($"{Api}/followups/{question.GetProperty("id").GetGuid()}/answer", new FollowUpAnswerRequest("accident")));
        await using (var db = runtime.Factory.Open())
        {
            var rule = await db.Ledger.SingleAsync(x => x.Origin == LedgerOrigin.Rule);
            Assert.Equal(LedgerKind.Edges, rule.Kind);
        }
        // Debug sessions never count: no questions, no ledger.
        runtime.Session.Abandon("reset");
        var debug = (await JsonAsync(await client.PostAsJsonAsync($"{Api}/session/start", StartBody(SessionMode.FreePlay, debug: true)))).GetProperty("session");
        await RecordOrgasmAsync(client, debug.GetProperty("sessionId").GetInt32());
        await runtime.Session.Ledger.ReconcileAsync(default, force: true);
        Assert.Empty((await JsonAsync(await client.GetAsync($"{Api}/state"))).GetProperty("followUps").EnumerateArray());
    }
}
