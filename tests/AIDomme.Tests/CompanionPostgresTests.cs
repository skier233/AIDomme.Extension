using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AIDomme;
using AIDomme.Persistence;
using AIDomme.Tracking;
using AIDomme.Data;
using AIDomme.Services;
using AIDomme.Sessions;
using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIDomme.Tests;

public sealed partial class TrackerPostgresTests
{
    private async Task InstallCompanionAsync()
    {
        await InstallGuidedAsync();
        await using var db = Context();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO users (\"Id\") VALUES (2)");
    }

    private async Task<WebApplication> CompanionAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<ICurrentPrincipalAccessor, CurrentPrincipalAccessor>();
        builder.Services.AddSingleton<Cove.Plugins.IExtensionServiceScopeFactory>(sp => new TestScopes(sp.GetRequiredService<IServiceScopeFactory>()));
        builder.Services.AddSingleton<GuidedRuntimes>();
        builder.Services.AddScoped<CoveContext>(_ => Context());
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<CoveContext>());
        builder.Services.AddScoped<TrackerService>();
        builder.Services.AddScoped<GuidedTrackerBridge>();
        builder.Services.AddScoped<GuidedConfiguration>();
        builder.Services.AddSingleton<IAuthorizationService, TrackerTests.TestAuthorization>();
        builder.Services.AddScoped<IUserEngagementService, UserEngagementService>();
        builder.Services.AddSingleton<MetronomeLeases>();
        builder.Services.AddSingleton<NarrationLeases>();
        builder.Services.AddSingleton<TtsOptions>();
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ITtsService, OrpheusTtsService>();
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:0");
        app.Use(async (context, next) =>
        {
            var userId = int.TryParse(context.Request.Headers["X-Test-User"], out var id) ? id : (int?)null;
            context.RequestServices.GetRequiredService<ICurrentPrincipalAccessor>().Set(userId is int user
                ? new CovePrincipal { UserId = user, Username = "test", Kind = PrincipalKind.User, Permissions = new HashSet<string> { "*" }, Roles = new HashSet<string>() }
                : CovePrincipal.Anonymous());
            try { await next(); }
            catch (Cove.Core.Auth.UnauthorizedException) { context.Response.StatusCode = 401; }
            catch (Exception ex) { context.Response.StatusCode = 500; await context.Response.WriteAsync(ex.ToString()); }
        });
        var extension = new AiDommeExtension();
        typeof(AiDommeExtension).GetField("_runtimes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(extension, app.Services.GetRequiredService<GuidedRuntimes>());
        extension.MapEndpoints(app);
        await app.StartAsync();
        return app;
    }
    private static HttpClient Client(WebApplication app, int user = 1)
    {
        var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        if (user > 0) client.DefaultRequestHeaders.Add("X-Test-User", user.ToString());
        return client;
    }
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.ReasonPhrase}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();
    }
    private static SessionContract Contract(SessionMode mode, bool debug = false, bool canChange = false) => SessionContract.Preset(mode) with
    {
        Preferences = new() { Intensity = 3, Length = 3, Denial = 3, Mean = 3 }, Debug = debug, CanChangeDuringSession = canChange,
    };
    private static object StartBody(SessionContract contract) => new { contract };
    private static object StartBody(SessionMode mode = SessionMode.FullGuided, bool debug = false) => StartBody(Contract(mode, debug));

    [PostgresTrackerFact]
    public async Task CompanionHttp_NonDebugSessionsCanBePausedButNotEnded()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync(); using var client = Client(app);
        var started = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody()));
        var session = started.GetProperty("session");
        Assert.False(session.GetProperty("isDebugSession").GetBoolean());
        var (id, step) = (session.GetProperty("sessionId").GetInt32(), session.GetProperty("stepNumber").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), id, step, "end"))).StatusCode);
        var paused = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), id, step, "pause")));
        Assert.True(paused.GetProperty("sessionActive").GetBoolean());
        Assert.True(paused.GetProperty("session").GetProperty("isPaused").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody())).StatusCode);

        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        runtime.Session.Abandon("test reset");
        var debug = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(debug: true)));
        session = debug.GetProperty("session");
        Assert.True(session.GetProperty("isDebugSession").GetBoolean());
        var ended = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control",
            new SessionControlRequest(Guid.NewGuid(), session.GetProperty("sessionId").GetInt32(), session.GetProperty("stepNumber").GetInt32(), "end")));
        Assert.False(ended.GetProperty("sessionActive").GetBoolean());
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_FreePlay_IsolatesAccounts_DeduplicatesControls_AndSeparatesPermissionFromFact()
    {
        await InstallCompanionAsync();
        await using var app = await CompanionAppAsync();
        using var first = Client(app); using var second = Client(app, 2); using var anonymous = Client(app, 0);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/ext/aidome/state")).StatusCode);
        var started = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(SessionMode.FreePlay)));
        var view = started.GetProperty("session");
        var session = view.GetProperty("sessionId").GetInt32();
        var step = view.GetProperty("stepNumber").GetInt32();
        Assert.Equal("FreePlay", view.GetProperty("mode").GetString());
        // Free play has no countdown the user sees, and no rest to beg for.
        Assert.Equal(-1, view.GetProperty("remainingSeconds").GetInt32());
        Assert.Null(started.GetProperty("stepEndsAtUtc").Deserialize<DateTime?>());
        Assert.False(view.GetProperty("canRequestRest").GetBoolean());
        Assert.False((await JsonAsync(await second.GetAsync("/api/ext/aidome/state"))).GetProperty("sessionActive").GetBoolean());
        var locked = new { sessionId = session, revision = 1, contract = Contract(SessionMode.FullGuided) };
        Assert.Equal(HttpStatusCode.Conflict, (await first.PutAsJsonAsync("/api/ext/aidome/session/contract", locked)).StatusCode);
        var pause = new SessionControlRequest(Guid.NewGuid(), session, step, "pause");
        var paused = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", pause));
        Assert.True(paused.GetProperty("session").GetProperty("isPaused").GetBoolean());
        var count = paused.GetProperty("messages").GetArrayLength();
        var repeated = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", pause));
        Assert.Equal(count, repeated.GetProperty("messages").GetArrayLength());
        // Asking while paused is refused by her, not applied.
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "request-permission")));
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        await using (var db = runtime.Factory.Open()) Assert.Empty(await db.CumRequests.ToListAsync());
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "resume")));
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "request-permission")));
        await using (var db = runtime.Factory.Open()) Assert.Single(await db.CumRequests.ToListAsync());

        // Permission is a decision; the orgasm is a fact recorded by the tracker.
        runtime.Session.State!.FinishDecided = true;
        runtime.Session.State.PermissionGranted = true;
        await using (var db = runtime.Factory.Open()) Assert.Empty(await db.Orgasms.ToListAsync());
        var tracker = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/tracker/initialize", new { }));
        var setting = tracker.GetProperty("settingId").GetGuid();
        var orgasm = new RecordTrackerEvent(Guid.NewGuid(), setting, "orgasm", DateTime.UtcNow, "button");
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/tracker/events", orgasm));
        Assert.False(runtime.Session.Active);
        await using (var db = runtime.Factory.Open())
        {
            Assert.True((await db.Orgasms.SingleAsync()).Allowed);
            Assert.Equal(SessionOutcome.AllowedOrgasm, (await db.Sessions.SingleAsync(x => x.Id == session)).Outcome);
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_EditableContracts_PersistAcceptedVersions_RejectStaleControls_AndRestorePaused()
    {
        await InstallCompanionAsync();
        int session, step, remaining;
        Guid operation = Guid.NewGuid(); string directive; long generation;
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var initial = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(Contract(SessionMode.OwnMethods, canChange: true))));
            session = initial.GetProperty("session").GetProperty("sessionId").GetInt32();
            var edit = new { sessionId = session, revision = 1, contract = Contract(SessionMode.FullGuided, canChange: true) };
            var changed = await JsonAsync(await client.PutAsJsonAsync("/api/ext/aidome/session/contract", edit));
            Assert.Equal(2, changed.GetProperty("session").GetProperty("contractRevision").GetInt32());
            Assert.Equal("FullGuided", changed.GetProperty("session").GetProperty("mode").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/ext/aidome/session/contract", edit)).StatusCode);
            step = changed.GetProperty("session").GetProperty("stepNumber").GetInt32();
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step - 1, "done"))).StatusCode);
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            generation = runtime.Session.State!.TimerGeneration;
            var paused = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(operation, session, step, "pause")));
            directive = paused.GetProperty("session").GetProperty("currentDirective").GetProperty("text").GetString()!;
            remaining = paused.GetProperty("session").GetProperty("remainingSeconds").GetInt32();
            await using var db = Context();
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::integer AS \"Value\" FROM aidomme_session_contracts WHERE \"UserId\" = {1}").SingleAsync());
            Assert.Equal("FullGuided", await db.Database.SqlQuery<string>($"SELECT \"Mode\" AS \"Value\" FROM aidomme_sessions WHERE \"Id\" = {session}").SingleAsync());
        }
        await using (var restored = await CompanionAppAsync())
        {
            using var client = Client(restored);
            var state = await JsonAsync(await client.GetAsync("/api/ext/aidome/state"));
            var view = state.GetProperty("session");
            Assert.True(view.GetProperty("isPaused").GetBoolean());
            Assert.Equal(session, view.GetProperty("sessionId").GetInt32());
            Assert.Equal(step, view.GetProperty("stepNumber").GetInt32());
            Assert.Equal(directive, view.GetProperty("currentDirective").GetProperty("text").GetString());
            Assert.Equal(remaining, view.GetProperty("remainingSeconds").GetInt32());
            var repeated = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(operation, session, step, "pause")));
            Assert.Equal(state.GetProperty("messages").GetArrayLength(), repeated.GetProperty("messages").GetArrayLength());
            // A timer armed before the restart is stale and changes nothing.
            var runtime = await restored.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            Assert.Empty(runtime.Session.Engine.Timer(runtime.Session.State!, generation).Say);
            Assert.Equal(step, runtime.Session.StepNumber);
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_NarrationOwnershipAndReceipts_PreventReplayAcrossRestarts()
    {
        await InstallCompanionAsync();
        var clientId = Guid.NewGuid(); var competitor = Guid.NewGuid(); Guid message;
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var state = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody()));
            message = state.GetProperty("messages").EnumerateArray().Last(x => x.GetProperty("sender").GetString() == "Domme").GetProperty("id").GetGuid();
            Assert.True((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/lease", new { clientId }))).GetProperty("granted").GetBoolean());
            Assert.False((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/lease", new { clientId = competitor }))).GetProperty("granted").GetBoolean());
            Assert.True((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/claim", new { clientId, messageId = message }))).GetProperty("granted").GetBoolean());
            Assert.False((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/claim", new { clientId, messageId = message }))).GetProperty("granted").GetBoolean());
            using var other = Client(app, 2);
            Assert.False((await JsonAsync(await other.PostAsJsonAsync("/api/ext/aidome/companion/audio/claim", new { clientId = competitor, messageId = message }))).GetProperty("granted").GetBoolean());
        }
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            await runtime.Session.ControlAsync("resume", null, default);
            Assert.True((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/lease", new { clientId = competitor }))).GetProperty("granted").GetBoolean());
            Assert.False((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/claim", new { clientId = competitor, messageId = message }))).GetProperty("granted").GetBoolean());
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_GameCheckpoint_PreservesTheGame_AndAContractChangeDoesNotFabricateALoss()
    {
        await InstallCompanionAsync();
        SessionView before;
        var games = Contract(SessionMode.FullGuided, debug: true, canChange: true) with { GamesOnly = true };
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(games)));
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            await runtime.Session.ControlAsync("pause", null, default);
            await runtime.SaveAsync(default);
            before = runtime.Session.View()!;
            Assert.NotNull(before.Game);
            Assert.True(runtime.Session.State!.Game!.RecordId > 0);
        }
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var state = await JsonAsync(await client.GetAsync("/api/ext/aidome/state"));
            // The secret never reaches the client.
            Assert.DoesNotContain("secret", state.GetProperty("session").GetProperty("game").GetRawText(), StringComparison.OrdinalIgnoreCase);
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            var after = runtime.Session.View()!;
            Assert.True(after.IsPaused);
            Assert.Equal(before.Game!.Type, after.Game!.Type);
            Assert.Equal(before.Game.Choices, after.Game.Choices);
            Assert.Equal(before.RemainingSeconds, after.RemainingSeconds);
            int record = runtime.Session.State!.Game!.RecordId;
            var free = Contract(SessionMode.FreePlay, debug: true, canChange: true);
            await JsonAsync(await client.PutAsJsonAsync("/api/ext/aidome/session/contract", new { sessionId = after.SessionId, revision = 1, contract = free }));
            Assert.Null(runtime.Session.View()!.Game);
            Assert.True(runtime.Session.View()!.IsPaused);
            await using var db = runtime.Factory.Open();
            Assert.Null((await db.GameChallenges.SingleAsync(x => x.Id == record)).Won);
            Assert.Empty(await db.Infractions.ToListAsync());
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_InterruptedCommand_IsNeverReplayedOrInterpretedAsBreach()
    {
        await InstallCompanionAsync();
        SessionControlRequest operation;
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var state = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody()));
            var view = state.GetProperty("session");
            operation = new(Guid.NewGuid(), view.GetProperty("sessionId").GetInt32(), view.GetProperty("stepNumber").GetInt32(), "request-permission");
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            await using var db = runtime.Factory.Open();
            db.Add(new CumRequest { SessionId = operation.SessionId, TimestampUtc = DateTime.UtcNow, Decision = true });
            await db.SaveChangesAsync();
            var payload = JsonSerializer.Serialize(operation, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO aidomme_session_controls (\"UserId\", \"EventId\", \"Payload\", \"Status\") VALUES ({1}, {operation.EventId}, {payload}, 'Pending')");
        }
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var state = await JsonAsync(await client.GetAsync("/api/ext/aidome/state"));
            Assert.False(state.GetProperty("sessionActive").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/control", operation)).StatusCode);
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            await using var db = runtime.Factory.Open();
            Assert.Single(await db.CumRequests.ToListAsync());
            Assert.Empty(await db.Infractions.ToListAsync());
            Assert.False((await db.Sessions.SingleAsync()).SuspectedUnauthorizedOrgasm);
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_LateActivity_PreservesFactsWithoutAdvancingNewTargetsOrReinterpretingPermission()
    {
        await InstallCompanionAsync();
        await using var app = await CompanionAppAsync(); using var client = Client(app);
        await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(Contract(SessionMode.FullGuided, canChange: true))));
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        var bridge = new GuidedTrackerBridge(app.Services.GetRequiredService<GuidedRuntimes>(), NullLogger<GuidedTrackerBridge>.Instance);
        async Task PersistUndeliveredAsync(TrackerEvent item)
        {
            await using var db = Context();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                db.Add(item); await bridge.RecordAsync(db, item, default); await db.SaveChangesAsync(); await transaction.CommitAsync();
            });
        }
        TrackerEvent Event(string kind) => new() { Id = Guid.NewGuid(), UserId = 1, SettingId = Guid.NewGuid(), Kind = kind, Source = "button", OccurredAtUtc = DateTime.UtcNow, ReceivedAtUtc = DateTime.UtcNow };
        var edge = Event("edge"); await PersistUndeliveredAsync(edge);
        var view = runtime.Session.View()!;
        // The step changes before the edge is delivered: it counts, but the new target is untouched.
        await runtime.Session.ChangeContractAsync(view.SessionId, view.ContractRevision, Contract(SessionMode.OwnMethods, canChange: true), default);
        await bridge.DeliverAsync(edge, default);
        Assert.Equal(1, runtime.Session.View()!.EdgesThisSession);
        Assert.Equal(0, runtime.Session.View()!.EdgesThisStep);
        await bridge.DeliverAsync(edge, default);
        Assert.Equal(1, runtime.Session.View()!.EdgesThisSession);

        // Permission stood when the orgasm was recorded; that decision is kept even if it lapses before delivery.
        runtime.Session.State!.FinishDecided = true;
        runtime.Session.State.PermissionGranted = true;
        var orgasm = Event("orgasm"); await PersistUndeliveredAsync(orgasm);
        runtime.Session.State.PermissionGranted = false;
        await bridge.DeliverAsync(orgasm, default);
        Assert.False(runtime.Session.Active);
        await using var verify = runtime.Factory.Open();
        Assert.True((await verify.Orgasms.SingleAsync()).Allowed);
        Assert.Empty(await verify.Infractions.ToListAsync());
    }
}
