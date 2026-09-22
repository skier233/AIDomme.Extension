using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using AIDome;
using AIDome.Persistence;
using AIDome.Tracking;
using AIDome.Data;
using AIDome.Services;
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

namespace AIDome.Tests;

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
        builder.Services.AddScoped<CommitmentStore>();
        builder.Services.AddScoped<ICommandCenterService>(sp => { var runtimes = sp.GetRequiredService<GuidedRuntimes>(); return new BorrowedCommandCenter(() => runtimes.Current.Commands); });
        builder.Services.AddSingleton<IAuthorizationService, TrackerTests.TestAuthorization>();
        builder.Services.AddScoped<IUserEngagementService, UserEngagementService>();
        builder.Services.AddSingleton<MetronomeLeases>();
        builder.Services.AddSingleton<NarrationLeases>();
        builder.Services.AddSingleton<MediaLeases>();
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
        var extension = new AiDomeExtension();
        typeof(AiDomeExtension).GetField("_runtimes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(extension, app.Services.GetRequiredService<GuidedRuntimes>());
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
    private static object StartBody(SessionPolicy policy, bool debug = false) => new { intensity = 3, length = 3, denial = 3, kink = 3, mean = 3, isDebugSession = debug, policy };

    [PostgresTrackerFact]
    public async Task Commitments_OnlyExplicitAcceptanceCommits_RetriesAreIdempotent_AndAccountsStayIsolated()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync();
        using var first = Client(app); using var second = Client(app, 2);
        object Mutate(Guid eventId, string action, Guid? targetId = null, decimal? amount = null, string? outcome = null, int? degree = null)
            => new { eventId, action, targetId, amount, outcome, degree };
        // Offers come from the domme, never from definitions the user writes.
        foreach (var action in new[] { "save-definition", "delete-definition", "propose" })
            Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), action, Guid.NewGuid()))).StatusCode);
        await JsonAsync(await first.GetAsync("/api/ext/aidome/state"));
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        var offer = await CommitmentStore.ProposeAsync(runtime.Factory, "Hold longer", "Hold every edge for 30 seconds.", "holds", 3, 2, null, default);
        var proposed = await JsonAsync(await first.GetAsync("/api/ext/aidome/commitments"));
        Assert.Equal("Proposed", proposed.GetProperty("offers")[0].GetProperty("status").GetString());
        Assert.Empty(proposed.GetProperty("commitments").EnumerateArray());
        Assert.Empty((await JsonAsync(await second.GetAsync("/api/ext/aidome/commitments"))).GetProperty("offers").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await second.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), "accept", offer.Id))).StatusCode);

        var acceptEvent = Guid.NewGuid();
        var accepted = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(acceptEvent, "accept", offer.Id))));
        foreach (var response in accepted) await JsonAsync(response);
        Assert.Equal(HttpStatusCode.BadRequest, (await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(acceptEvent, "decline", offer.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), "accept", offer.Id))).StatusCode);
        var state = await JsonAsync(await first.GetAsync("/api/ext/aidome/commitments"));
        var commitment = Assert.Single(state.GetProperty("commitments").EnumerateArray());
        Assert.NotNull(commitment.GetProperty("dueAtUtc").Deserialize<DateTime?>());
        var commitmentId = commitment.GetProperty("id").GetGuid();
        var progressed = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), "progress", commitmentId, amount: 2)));
        Assert.Equal(2, progressed.GetProperty("commitments")[0].GetProperty("progress").GetDecimal());
        var reported = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), "report", commitmentId, outcome: "Partial", degree: 60)));
        Assert.Equal("Partial", reported.GetProperty("commitments")[0].GetProperty("status").GetString());
        Assert.Equal(60, reported.GetProperty("reports")[0].GetProperty("degree").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/ext/aidome/commitments/mutate", Mutate(Guid.NewGuid(), "report", commitmentId, outcome: "Completed", degree: 100))).StatusCode);
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_NonDebugSessionsCanBePausedButNotEnded()
    {
        await InstallCompanionAsync(); await using var app = await CompanionAppAsync(); using var client = Client(app);
        var started = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(new SessionPolicy())));
        var online = started.GetProperty("online");
        Assert.False(online.GetProperty("isDebugSession").GetBoolean());
        var (session, step) = (online.GetProperty("sessionId").GetInt32(), online.GetProperty("stepNumber").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "end"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/ext/aidome/session/end", null)).StatusCode);
        var paused = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "pause")));
        Assert.True(paused.GetProperty("sessionActive").GetBoolean());
        Assert.True(paused.GetProperty("online").GetProperty("isPaused").GetBoolean());

        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        runtime.Online.EndSession();
        var debug = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(new SessionPolicy(), debug: true)));
        online = debug.GetProperty("online");
        Assert.True(online.GetProperty("isDebugSession").GetBoolean());
        var ended = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control",
            new SessionControlRequest(Guid.NewGuid(), online.GetProperty("sessionId").GetInt32(), online.GetProperty("stepNumber").GetInt32(), "end")));
        Assert.False(ended.GetProperty("sessionActive").GetBoolean());
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_LocksPolicies_IsolatesAccounts_DeduplicatesControls_AndSeparatesPermissionFromFact()
    {
        await InstallCompanionAsync();
        await using var app = await CompanionAppAsync();
        using var first = Client(app); using var second = Client(app, 2); using var anonymous = Client(app, 0);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/ext/aidome/state")).StatusCode);
        var permission = new SessionPolicy { Guidance = "Permission", Pacing = "Free", Permission = "RequestsOnly", GamesEnabled = false };
        var started = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(permission)));
        var session = started.GetProperty("online").GetProperty("sessionId").GetInt32();
        var step = started.GetProperty("online").GetProperty("stepNumber").GetInt32();
        Assert.Null(started.GetProperty("stepEndsAtUtc").Deserialize<DateTime?>());
        Assert.False((await JsonAsync(await second.GetAsync("/api/ext/aidome/state"))).GetProperty("sessionActive").GetBoolean());
        var edit = new { sessionId = session, revision = 1, policy = permission with { Media = "Engine" } };
        Assert.Equal(HttpStatusCode.Conflict, (await first.PutAsJsonAsync("/api/ext/aidome/session/policy", edit)).StatusCode);
        var pause = new SessionControlRequest(Guid.NewGuid(), session, step, "pause");
        var paused = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", pause));
        Assert.True(paused.GetProperty("online").GetProperty("isPaused").GetBoolean());
        var count = paused.GetProperty("messages").GetArrayLength();
        var repeated = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", pause));
        Assert.Equal(count, repeated.GetProperty("messages").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await first.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "beg-to-cum"))).StatusCode);
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step, "resume")));
        var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
        runtime.Online.GrantPermission(); await runtime.SaveAsync(default);
        await using (var db = runtime.Factory.Open()) Assert.Empty(await db.Orgasms.ToListAsync());
        var tracker = await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/tracker/initialize", new { }));
        var setting = tracker.GetProperty("settingId").GetGuid();
        var orgasm = new RecordTrackerEvent(Guid.NewGuid(), setting, "orgasm", DateTime.UtcNow, "button");
        await JsonAsync(await first.PostAsJsonAsync("/api/ext/aidome/tracker/events", orgasm));
        Assert.False(runtime.Online.HasActiveSession);
        await using (var db = runtime.Factory.Open()) Assert.True((await db.Orgasms.SingleAsync()).Allowed);
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_EditableModes_PersistAcceptedVersions_RejectStaleControls_AndRestorePaused()
    {
        await InstallCompanionAsync();
        int session, step, remaining;
        Guid operation = Guid.NewGuid(); string directive;
        var policy = new SessionPolicy { Guidance = "Simple", CanChangeDuringSession = true, GamesEnabled = false, AllowedMethodIds = [7] };
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            var initial = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(policy)));
            session = initial.GetProperty("online").GetProperty("sessionId").GetInt32();
            var edit = new { sessionId = session, revision = 1, policy = policy with { Pacing = "Free", Permission = "RequestsOnly" } };
            var changed = await JsonAsync(await client.PutAsJsonAsync("/api/ext/aidome/session/policy", edit));
            Assert.Equal(2, changed.GetProperty("policyRevision").GetInt32());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/api/ext/aidome/session/policy", edit)).StatusCode);
            step = changed.GetProperty("online").GetProperty("stepNumber").GetInt32();
            Assert.Equal(0, changed.GetProperty("online").GetProperty("currentDirective").GetProperty("speed").GetInt32());
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(Guid.NewGuid(), session, step - 1, "done"))).StatusCode);
            var paused = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(operation, session, step, "pause")));
            directive = paused.GetProperty("online").GetProperty("currentDirective").GetProperty("text").GetString()!;
            remaining = paused.GetProperty("online").GetProperty("remainingSeconds").GetInt32();
            await using var db = Context();
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::integer AS \"Value\" FROM aidomme_session_policies WHERE \"UserId\" = {1}").SingleAsync());
        }
        await using (var restored = await CompanionAppAsync())
        {
            using var client = Client(restored);
            var state = await JsonAsync(await client.GetAsync("/api/ext/aidome/state"));
            var online = state.GetProperty("online");
            Assert.True(online.GetProperty("isPaused").GetBoolean());
            Assert.Equal(session, online.GetProperty("sessionId").GetInt32());
            Assert.Equal(step, online.GetProperty("stepNumber").GetInt32());
            Assert.Equal(directive, online.GetProperty("currentDirective").GetProperty("text").GetString());
            Assert.Equal(remaining, online.GetProperty("remainingSeconds").GetInt32());
            var repeated = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/control", new SessionControlRequest(operation, session, step, "pause")));
            Assert.Equal(state.GetProperty("messages").GetArrayLength(), repeated.GetProperty("messages").GetArrayLength());
            var runtime = await restored.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            typeof(OnlineSessionEngine).GetMethod("OnStepTimerElapsed", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(runtime.Online, [0L]);
            Assert.Equal(step, runtime.Online.GetCurrentState().StepNumber);
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
            var state = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(new SessionPolicy())));
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
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1); runtime.Online.Resume();
            Assert.True((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/lease", new { clientId = competitor }))).GetProperty("granted").GetBoolean());
            Assert.False((await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/companion/audio/claim", new { clientId = competitor, messageId = message }))).GetProperty("granted").GetBoolean());
        }
    }

    [PostgresTrackerFact]
    public async Task CompanionHttp_GameCheckpoint_PreservesChallenge_AndModeChangeDoesNotFabricateFailure()
    {
        await InstallCompanionAsync();
        OnlineSessionState before;
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", new { intensity = 3, length = 3, denial = 3, kink = 3, mean = 3, onlyGamesMode = true, policy = new SessionPolicy { CanChangeDuringSession = true } }));
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            runtime.Online.Pause(); await runtime.SaveAsync(default);
            before = runtime.Online.GetCurrentState();
            Assert.NotNull(before.ActiveGameChallengeId);
        }
        await using (var app = await CompanionAppAsync())
        {
            using var client = Client(app);
            await JsonAsync(await client.GetAsync("/api/ext/aidome/state"));
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            var after = runtime.Online.GetCurrentState();
            Assert.True(after.IsPaused);
            Assert.Equal(before.ActiveGameChallengeId, after.ActiveGameChallengeId);
            Assert.Equal(before.ActiveGameType, after.ActiveGameType);
            Assert.Equal(before.ActiveGameRange, after.ActiveGameRange);
            Assert.Equal(before.ActiveGameAnswerReady, after.ActiveGameAnswerReady);
            Assert.Equal(before.RemainingSeconds, after.RemainingSeconds);
            var policy = new SessionPolicy { Guidance = "Permission", Pacing = "Free", Permission = "RequestsOnly", GamesEnabled = false, CanChangeDuringSession = true };
            await JsonAsync(await client.PutAsJsonAsync("/api/ext/aidome/session/policy", new { sessionId = after.SessionId, revision = 1, policy }));
            Assert.Null(runtime.Online.GetCurrentState().ActiveGameChallengeId);
            Assert.True(runtime.Online.GetCurrentState().IsPaused);
            await using var db = runtime.Factory.Open();
            Assert.Null((await db.GameChallenges.SingleAsync(x => x.Id == before.ActiveGameChallengeId)).Won);
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
            var state = await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(new SessionPolicy())));
            operation = new(Guid.NewGuid(), state.GetProperty("online").GetProperty("sessionId").GetInt32(), state.GetProperty("online").GetProperty("stepNumber").GetInt32(), "beg-to-cum");
            var runtime = await app.Services.GetRequiredService<GuidedRuntimes>().ForUserAsync(1);
            await runtime.Interaction.RecordCumRequestAsync(true);
            await using var db = runtime.Factory.Open();
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
        var policy = new SessionPolicy { Guidance = "Simple", CanChangeDuringSession = true, GamesEnabled = false, AllowedMethodIds = [7] };
        await JsonAsync(await client.PostAsJsonAsync("/api/ext/aidome/session/start", StartBody(policy)));
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
        await runtime.Online.SetPolicyAsync(policy with { Pacing = "Free" }, true, default);
        await bridge.DeliverAsync(edge, default);
        Assert.Equal(1, runtime.Online.GetCurrentState().EdgesThisSession);
        Assert.Equal(0, typeof(OnlineSessionEngine).GetField("_edgesThisStep", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime.Online));
        await bridge.DeliverAsync(edge, default);
        Assert.Equal(1, runtime.Online.GetCurrentState().EdgesThisSession);
        runtime.Online.GrantPermission();
        var orgasm = Event("orgasm"); await PersistUndeliveredAsync(orgasm);
        await runtime.Online.SetPolicyAsync(policy, true, default);
        Assert.NotEqual(SessionPhase.Finishing, runtime.Online.GetCurrentState().Phase);
        await bridge.DeliverAsync(orgasm, default);
        Assert.False(runtime.Online.HasActiveSession);
        await using var verify = runtime.Factory.Open();
        Assert.True((await verify.Orgasms.SingleAsync()).Allowed);
        Assert.Empty(await verify.Infractions.ToListAsync());
    }
}
