using AIDome.Tracking;
using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Cove.Data.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using AIDome.Persistence;
using AIDome.Data;
using AIDome.Services;
using AIDome.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIDome.Tests;

public sealed class PostgresTrackerFactAttribute : FactAttribute
{
    public PostgresTrackerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIDOMME_TEST_POSTGRES")))
            Skip = "Set AIDOMME_TEST_POSTGRES to a local PostgreSQL maintenance connection to run isolated integration tests.";
    }
}

/// <summary>Extension-owned scopes for tests, standing in for the host's tracked scope factory.</summary>
public sealed class TestScopes(IServiceScopeFactory scopes) : Cove.Plugins.IExtensionServiceScopeFactory
{
    public IServiceScope CreateScope() => scopes.CreateScope();
    public AsyncServiceScope CreateAsyncScope() => scopes.CreateAsyncScope();
}

// Creates and drops only an explicitly named, random test database. Never installs tables into
// the connection's original database and never reads Cove's actual users or media.
public sealed partial class TrackerPostgresTests : IAsyncLifetime
{
    private readonly string database = "aidomme_tracker_test_" + Guid.NewGuid().ToString("N");
    private readonly NpgsqlConnectionStringBuilder maintenance = new(Environment.GetEnvironmentVariable("AIDOMME_TEST_POSTGRES") ?? "");
    private string connection = "";
    private bool created;

    public async Task InitializeAsync()
    {
        maintenance.Database = "postgres";
        maintenance.Pooling = false;
        await using var admin = new NpgsqlConnection(maintenance.ConnectionString);
        await admin.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin)) await command.ExecuteNonQueryAsync();
        created = true;
        connection = new NpgsqlConnectionStringBuilder(maintenance.ConnectionString) { Database = database }.ConnectionString;
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        // Cove's own tables come from the trimmed model; AI Domme's come from its real migrations.
        await db.Database.ExecuteSqlRawAsync("""
            DO $$ DECLARE name text; BEGIN
              FOR name IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename LIKE 'aidomme\_%' LOOP
                EXECUTE format('DROP TABLE %I CASCADE', name);
              END LOOP;
            END $$;
            CREATE TABLE users ("Id" integer PRIMARY KEY);
            INSERT INTO users ("Id") VALUES (1);
            INSERT INTO "Videos" ("Id") VALUES (1);
            INSERT INTO "Images" ("Id") VALUES (1);
            """);
        await db.Database.ExecuteSqlRawAsync(TrackerSchema.Sql);
        // The actual forward migration is safe to run again.
        await db.Database.ExecuteSqlRawAsync(TrackerSchema.Sql);
    }

    private TrackerTests.TestContext Context() => new(new DbContextOptionsBuilder<TrackerTests.TestContext>()
        .UseNpgsql(connection, x => x.EnableRetryOnFailure()).Options);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    /// <summary>Applies 002-010 exactly as Cove would on a fresh install.</summary>
    private async Task InstallGuidedAsync()
    {
        await using var db = Context();
        foreach (var sql in new[] { GuidedMigrations.Superseded, GuidedMigrations.Superseded, GuidedMigrations.Superseded, GuidedMigrations.Load("005_guided_v2"),
                     GuidedMigrations.Load("006_session_contracts"), GuidedMigrations.Load("007_ledger"), GuidedMigrations.Load("008_voice"), GuidedMigrations.Load("009_pace"), GuidedMigrations.Load("010_offer_values") })
            await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>The schema an install had before 005: the frozen 002-004 scripts.</summary>
    private async Task InstallLegacyGuidedAsync()
    {
        await using var db = Context();
        foreach (var name in new[] { "legacy-002_guided.sql", "legacy-003_companion.sql", "legacy-004_commitments.sql" })
            await db.Database.ExecuteSqlRawAsync(Fixture(name));
    }

    private ServiceProvider GuidedServices() => new ServiceCollection()
        .AddScoped<DbContext>(_ => Context())
        .BuildServiceProvider();

    private static CurrentPrincipalAccessor Principal(int user)
    {
        var principal = new CurrentPrincipalAccessor();
        principal.Set(new CovePrincipal { UserId = user, Username = "isolated", Kind = PrincipalKind.User, Permissions = new HashSet<string> { "*" }, Roles = new HashSet<string>() });
        return principal;
    }

    private static TrackerService Service(TrackerTests.TestContext db)
    {
        var principal = new CurrentPrincipalAccessor();
        principal.Set(new CovePrincipal { UserId = 1, Username = "isolated-test", Kind = PrincipalKind.User,
            Permissions = new HashSet<string> { "*" }, Roles = new HashSet<string>() });
        return new(db, principal, new TrackerTests.TestAuthorization(), new UserEngagementService(db, principal));
    }

    [PostgresTrackerFact]
    public async Task Migrations005To007_UpgradeLegacyRows_KeepIdentities_AndCarryPunishmentsIntoTheLedger()
    {
        await InstallLegacyGuidedAsync();
        var eventId = Guid.NewGuid(); var offerId = Guid.NewGuid(); var commitmentId = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            format = 1, revision = 3,
            definitions = new[] { new { id = Guid.NewGuid(), name = "Authored", terms = "User-written", unit = "items", target = 1 } },
            offers = new[] { new { id = offerId, definitionId = Guid.NewGuid(), name = "Hold", terms = "Hold 30s", unit = "holds", target = 2, dueAfterDays = (int?)null, dueAtUtc = (DateTime?)null, status = "Accepted", originSessionId = 5, createdAtUtc = DateTime.UtcNow, acceptedAtUtc = DateTime.UtcNow } },
            commitments = new[] { new { id = commitmentId, offerId, name = "Hold", terms = "Hold 30s", unit = "holds", target = 2, progress = 1, dueAtUtc = (DateTime?)null, status = "Active", acceptedAtUtc = DateTime.UtcNow, resolvedAtUtc = (DateTime?)null } },
            reports = Array.Empty<object>(), operations = new Dictionary<Guid, string>(),
        });
        const string checkpoint = "{\"format\":1}", empty = "{}";
        await using (var db = Context())
        {
            await db.Database.ExecuteSqlRawAsync("INSERT INTO users (\"Id\") VALUES (2)");
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_guided_accounts ("OwnerId", "UserId") VALUES (1, 1), (2, 2);
                UPDATE aidomme_guided_stimmethod SET "TimePointMultiplier" = 0.123 WHERE "OwnerId" = 0 AND "Id" = 1;
                INSERT INTO aidomme_guided_stimmethod SELECT 1, "Id", "Name", "Intensity", "Tease", "TortLevel", "Closeness", "RequiresLube", "ToyCategory", "Description", "TimePointMultiplier", "EdgePointMultiplier", FALSE FROM aidomme_guided_stimmethod WHERE "OwnerId" = 0;
                INSERT INTO aidomme_guided_stimmethod SELECT 2, "Id", "Name", "Intensity", "Tease", "TortLevel", "Closeness", "RequiresLube", "ToyCategory", "Description", "TimePointMultiplier", "EdgePointMultiplier", FALSE FROM aidomme_guided_stimmethod WHERE "OwnerId" = 0;
                UPDATE aidomme_guided_stimmethod SET "Description" = 'My own wording', "IsOverride" = TRUE WHERE "OwnerId" = 1 AND "Id" = 2;
                UPDATE aidomme_guided_stimmethod SET "EdgePointMultiplier" = 4.5 WHERE "OwnerId" = 2 AND "Id" = 3;
                INSERT INTO aidomme_guided_tuningcomparisonentry ("OwnerId", "Id", "Mode", "WinnerKey", "LoserKey", "Strength", "TimestampUtc") VALUES (0, 1, 'Stim', 'method:1', 'method:2', 3, now());
                INSERT INTO aidomme_guided_sessionrecord ("OwnerId", "Id", "StartedAtUtc", "LastInteractionUtc", "AllowSelfOral", "Intensity") VALUES (1, 5, now(), now(), FALSE, 7);
                INSERT INTO aidomme_guided_edgeentry ("OwnerId", "Id", "TimestampUtc", "StimMethodId", "HeldDuration", "SessionId") VALUES (1, 9, now(), 2, 12.5, 5);
                INSERT INTO aidomme_guided_punishmententry ("OwnerId", "Id", "IssuedAtUtc", "SessionId", "Category", "Tier", "Description", "RemainingCount", "DenialEndsAtUtc", "Completed")
                  VALUES (1, 3, now() - interval '1 day', 5, 0, 4, 'No cumming for 3 days', 0, now() + interval '2 days', FALSE),
                         (1, 4, now() - interval '2 days', 5, 2, 6, 'Overload session', 2, NULL, FALSE);
                INSERT INTO aidomme_tracker_events ("Id", "UserId", "SettingId", "Kind", "OccurredAtUtc", "ReceivedAtUtc", "Source", "LikeWeight", "LikeIdsJson") VALUES ({eventId}, 1, gen_random_uuid(), 'edge', now(), now(), 'hotkey', 0, '[]');
                INSERT INTO aidomme_guided_guidedeventreceipt ("OwnerId", "Id", "EventId", "SessionId", "Kind", "ActivityId", "DeliveredAtUtc") VALUES (1, 4, {eventId}, 5, 'edge', 9, now());
                INSERT INTO aidomme_guided_event_steps ("OwnerId", "EventId", "StepNumber") VALUES (1, {eventId}, 6);
                INSERT INTO aidomme_guided_recovery ("OwnerId", "Revision", "Payload", "UpdatedAtUtc") VALUES (1, 77, {checkpoint}, now());
                INSERT INTO aidomme_guided_policies ("OwnerId", "SessionId", "Revision", "Policy", "AcceptedAtUtc") VALUES (1, 5, 1, {empty}, now());
                INSERT INTO aidomme_guided_controls ("OwnerId", "EventId", "Payload", "Status") VALUES (1, gen_random_uuid(), {empty}, 'Applied');
                INSERT INTO aidomme_commitment_state ("OwnerId", "Revision", "Payload", "UpdatedAtUtc") VALUES (1, 3, {payload}, now());
                """);
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("005_guided_v2"));
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("006_session_contracts"));
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("007_ledger"));
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("008_voice"));
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("009_pace"));
            await db.Database.ExecuteSqlRawAsync(GuidedMigrations.Load("010_offer_values"));
        }
        await using (var db = Context())
        {
            async Task<int> Count(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
            Assert.Equal(0, await Count("SELECT COUNT(*)::int AS \"Value\" FROM pg_tables WHERE tablename LIKE 'aidomme\\_guided%' OR tablename IN ('aidomme_commitment_state', 'aidomme_narration_receipts')"));
            Assert.Equal(40, await Count("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_methods WHERE \"UserId\" IS NULL"));
            // Identical personal copies are not overrides; edited or saved-as-override copies are.
            Assert.Equal(2, await Count("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_methods WHERE \"UserId\" IS NOT NULL"));
            var services = GuidedServices();
            var first = new GuidedDataFactory(new TestScopes(services.GetRequiredService<IServiceScopeFactory>()), 1);
            var second = new GuidedDataFactory(new TestScopes(services.GetRequiredService<IServiceScopeFactory>()), 2);
            await using (var a = first.Open())
            {
                var methods = await a.MethodsAsync();
                Assert.Equal(40, methods.Count);
                Assert.Equal(0.123, methods.Single(x => x.Id == 1).TimePointMultiplier);
                Assert.Equal("My own wording", methods.Single(x => x.Id == 2).Description);
                var session = await a.Sessions.SingleAsync();
                Assert.Equal((5, 7), (session.Id, session.Intensity));
                var edge = await a.Edges.SingleAsync();
                Assert.Equal((9, 2, 12.5), (edge.Id, edge.StimMethodId, edge.HeldDuration.TotalSeconds));
                var receipt = await a.TrackerReceipts.SingleAsync();
                Assert.Equal((eventId, 6), (receipt.EventId, receipt.StepNumber));
                Assert.Equal(1, await a.TuningComparisons.CountAsync());
                // Free-form offers and commitments cannot be enforced; they are kept aside, not converted.
                Assert.Empty(await a.Offers.ToListAsync());
                Assert.Empty(await a.Commitments.ToListAsync());
                Assert.Equal(1, await Count($"SELECT COUNT(*)::int AS \"Value\" FROM aidomme_legacy_offers WHERE \"Id\" = '{offerId}'"));
                Assert.Equal(1, await Count($"SELECT COUNT(*)::int AS \"Value\" FROM aidomme_legacy_commitments WHERE \"Id\" = '{commitmentId}'"));
                Assert.Equal(0, await Count("SELECT COUNT(*)::int AS \"Value\" FROM pg_tables WHERE tablename IN ('aidomme_punishments', 'aidomme_commitment_operations')"));
                var owed = await a.Ledger.OrderBy(x => x.CreatedAtUtc).ToListAsync();
                Assert.Equal(2, owed.Count);
                Assert.Equal((AIDome.Sessions.LedgerKind.PunishmentSession, 2, AIDome.Sessions.LedgerOrigin.Legacy, AIDome.Sessions.LedgerStatus.Owed),
                    (owed[0].Kind, owed[0].Remaining, owed[0].Origin, owed[0].Status));
                Assert.Equal((AIDome.Sessions.LedgerKind.Denial, "No cumming for 3 days", AIDome.Sessions.LedgerStatus.Owed), (owed[1].Kind, owed[1].Reason, owed[1].Status));
                Assert.NotNull(owed[1].EndsAtUtc);
                Assert.Equal(10, (await a.OfferDefinitionsAsync()).Count);
            }
            await using (var b = second.Open())
            {
                var methods = await b.MethodsAsync();
                Assert.Equal(4.5, methods.Single(x => x.Id == 3).EdgePointMultiplier);
                Assert.NotEqual("My own wording", methods.Single(x => x.Id == 2).Description);
                Assert.Empty(await b.Sessions.ToListAsync());
                Assert.Empty(await b.Offers.ToListAsync());
            }
            Assert.Equal(77, await Count("SELECT \"Revision\"::int AS \"Value\" FROM aidomme_runtime_checkpoints WHERE \"UserId\" = 1"));
            Assert.Equal(1, await Count("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_session_contracts"));
            Assert.Equal(1, await Count("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_session_controls"));
            // New rows continue after the copied identities.
            await using (var a = first.Open())
            {
                var added = a.Add(new SessionRecord { StartedAtUtc = DateTime.UtcNow, LastInteractionUtc = DateTime.UtcNow });
                await a.SaveChangesAsync();
                Assert.True(added.Id > 5);
            }
        }
    }

    [PostgresTrackerFact]
    public async Task EveryMappedEntity_MatchesTheMigratedTables()
    {
        await InstallGuidedAsync();
        await using var db = Context();
        foreach (var type in GuidedSchema.EntityTypes)
        {
            // Selecting every mapped column fails if the model and the literal migration disagree.
            var query = (IQueryable<object>)typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!.MakeGenericMethod(type).Invoke(db, null)!;
            await query.Take(1).ToListAsync();
        }
        Assert.Equal(40, await db.Set<StimMethod>().CountAsync());
        Assert.Equal(14, await db.Set<PunishmentEnder>().CountAsync());
    }

    [PostgresTrackerFact]
    public async Task GuidedRuntime_IsolatesAccounts_OverridesDefinitions_AndConsumesTrackerOnce()
    {
        await InstallGuidedAsync();
        await using (var setup = Context()) await setup.Database.ExecuteSqlRawAsync("INSERT INTO users (\"Id\") VALUES (2)");
        await using var services = GuidedServices();
        var principal = Principal(1);
        await using var runtimes = new GuidedRuntimes(new TestScopes(services.GetRequiredService<IServiceScopeFactory>()), principal, NullLoggerFactory.Instance);
        var first = await runtimes.ForUserAsync(1);
        var second = await runtimes.ForUserAsync(2);
        Assert.Same(first, await runtimes.ForUserAsync(1));

        // A saved definition is a personal override; the other account keeps the shared default.
        var configuration = new GuidedConfiguration(runtimes);
        await runtimes.EnsureCurrentAsync(default);
        var shared = (await first.Factory.Open().MethodsAsync()).Single(x => x.Id == 1);
        var edited = shared.AsDefinition(1);
        edited.TimePointMultiplier = 0.456;
        await configuration.SaveAsync(1, edited, default);
        await using (var a = first.Factory.Open()) Assert.Equal(0.456, (await a.MethodsAsync()).Single(x => x.Id == 1).TimePointMultiplier);
        await using (var b = second.Factory.Open()) Assert.Equal(shared.TimePointMultiplier, (await b.MethodsAsync()).Single(x => x.Id == 1).TimePointMultiplier);
        await configuration.SaveAsync(1, edited, default);
        await using (var a = first.Factory.Open()) Assert.Equal(1, await a.Db.Set<StimMethod>().CountAsync(x => x.UserId == 1));
        await configuration.ResetAsync<StimMethod>(1, default);
        await using (var a = first.Factory.Open())
        {
            Assert.Equal(shared.TimePointMultiplier, (await a.MethodsAsync()).Single(x => x.Id == 1).TimePointMultiplier);
            Assert.Equal(0, await a.Db.Set<StimMethod>().CountAsync(x => x.UserId == 1));
        }

        var contract = SessionContract.Preset(SessionMode.FullGuided) with { Preferences = new() { Intensity = 3, Length = 3, Denial = 3, Mean = 3 } };
        await first.Session.StartAsync(contract, default);
        Assert.True(first.Session.Active);
        Assert.False(second.Session.Active);
        var sessionId = first.Session.ActiveSessionId!.Value;
        Assert.NotEmpty(await first.Session.GenerateHomeworkAsync(contract with { Preferences = contract.Preferences with { Intensity = 8 } }, false, default));
        Assert.Equal(sessionId, first.Session.ActiveSessionId);
        await using (var b = second.Factory.Open())
        {
            Assert.Empty(await b.Sessions.ToListAsync());
            Assert.Empty(await b.InstructionSteps.ToListAsync());
        }

        var bridge = new GuidedTrackerBridge(runtimes, NullLogger<GuidedTrackerBridge>.Instance);
        await using var trackerDb = Context();
        var tracking = new AIDome.Tracking.TrackerService(trackerDb, principal, new TrackerTests.TestAuthorization(), new UserEngagementService(trackerDb, principal), bridge);
        var prefs = await tracking.SettingsAsync(new(false, 0, 1), false, default);
        var request = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "edge", DateTime.UtcNow, "hotkey");
        int stepAtEdge = first.Session.StepNumber;
        await tracking.RecordAsync(request, default);
        await tracking.RecordAsync(request, default);
        var metrics = await first.Factory.ReadTrackerMetricsAsync(DateTime.UtcNow, default);
        Assert.Equal(1, metrics.EdgesSinceLastOrgasm);
        Assert.Null(metrics.LastOrgasmAtUtc);
        Assert.Equal(1, first.Session.View()!.EdgesThisSession);
        await using (var a = first.Factory.Open())
        {
            Assert.Single(await a.Edges.ToListAsync());
            var receipt = await a.TrackerReceipts.SingleAsync();
            Assert.Equal(stepAtEdge, receipt.StepNumber);
        }
        await tracking.UndoAsync(request.Id, default);
        Assert.False(first.Session.Active);
        await using (var a = first.Factory.Open())
        {
            Assert.Empty(await a.Edges.ToListAsync());
            Assert.False((await a.Sessions.SingleAsync(x => x.Id == sessionId)).SuspectedUnauthorizedOrgasm);
        }
        var passive = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "orgasm", DateTime.UtcNow, "button");
        await tracking.RecordAsync(passive, default);
        metrics = await first.Factory.ReadTrackerMetricsAsync(DateTime.UtcNow, default);
        Assert.NotNull(metrics.LastOrgasmAtUtc);
        Assert.Equal(0, metrics.EdgesSinceLastOrgasm);
        await using (var a = first.Factory.Open()) Assert.Empty(await a.Orgasms.ToListAsync());

        // A guided write failure rolls back the tracker event and its likes.
        await first.Session.StartAsync(contract with { Debug = true }, default);
        var staleSession = first.Session.ActiveSessionId!.Value;
        await using (var a = first.Factory.Open())
            await a.Sessions.Where(x => x.Id == staleSession).ExecuteDeleteAsync();
        await tracking.SettingsAsync(new(true, 1, 3), false, default);
        var failed = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "edge", DateTime.UtcNow, "button", "video", 1);
        await Assert.ThrowsAsync<AIDome.Tracking.TrackerValidationException>(() => tracking.RecordAsync(failed, default));
        trackerDb.ChangeTracker.Clear();
        Assert.False(await trackerDb.Set<AIDome.Tracking.TrackerEvent>().AnyAsync(x => x.Id == failed.Id));
        Assert.Empty(await trackerDb.Interactions.ToListAsync());
        Assert.Empty(await trackerDb.UserEntityAffinities.ToListAsync());
        Assert.Equal(0, first.Session.View()!.EdgesThisSession);
        first.Session.Abandon("test done");

        // Deleting the Cove account removes its AI Domme rows.
        await trackerDb.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE \"Id\" = 1");
        Assert.Equal(0, await trackerDb.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_sessions WHERE \"UserId\" = 1").SingleAsync());
    }

    [PostgresTrackerFact]
    public async Task ConcurrentDuplicateDeliveries_CommitOneEventAndThreeLikes()
    {
        await using var setup = Context();
        var preferences = await Service(setup).SettingsAsync(new(true, 1, 3), false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), preferences.SettingId, "orgasm", DateTime.UtcNow, "button", "video", 1);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Context();
            await Service(db).RecordAsync(request, default);
        }));
        await using var verify = Context();
        Assert.Equal(1, await verify.Set<TrackerEvent>().CountAsync());
        Assert.Equal(3, await verify.Interactions.CountAsync());
        Assert.Equal(3, (await verify.UserEntityAffinities.SingleAsync()).LikeCount);
        await Service(verify).UndoAsync(request.Id, default);
        Assert.Equal(0, await verify.Interactions.CountAsync());
        Assert.Equal(0, (await verify.UserEntityAffinities.SingleAsync()).LikeCount);
    }

    [PostgresTrackerFact]
    public async Task ConcurrentNativeLikesAndTrackerEvents_PreserveTheSharedCounter()
    {
        await using var setup = Context();
        var preferences = await Service(setup).SettingsAsync(new(true, 1, 3), false, default);
        // Ensure the affinity already exists, covering read/modify/write races as well as first use.
        await Service(setup).RecordAsync(new(Guid.NewGuid(), preferences.SettingId, "edge", DateTime.UtcNow, "button", "video", 1), default);
        await Task.WhenAll(Enumerable.Range(0, 16).Select(async i =>
        {
            await using var db = Context();
            var tracker = Service(db);
            if (i % 2 == 0)
                await tracker.RecordAsync(new(Guid.NewGuid(), preferences.SettingId, "orgasm", DateTime.UtcNow, "hotkey", "video", 1), default);
            else
                await new UserEngagementService(db, new CurrentPrincipalAccessor()).IncrementVideoLikeAsync(1);
        }));
        await using var verify = Context();
        Assert.Equal(33, await verify.Interactions.CountAsync());
        Assert.Equal(33, (await verify.UserEntityAffinities.SingleAsync()).LikeCount);
    }

    [PostgresTrackerFact]
    public async Task DistinctConcurrentEvents_OnOneImage_PreserveEveryWeight()
    {
        await using var setup = Context();
        var preferences = await Service(setup).SettingsAsync(new(true, 1, 2), false, default);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var db = Context();
            await Service(db).RecordAsync(new(Guid.NewGuid(), preferences.SettingId, "orgasm", DateTime.UtcNow, "hotkey", "image", 1), default);
        }));
        await using var verify = Context();
        Assert.Equal(8, await verify.Set<TrackerEvent>().CountAsync());
        Assert.Equal(16, await verify.Interactions.CountAsync());
        Assert.Equal(16, (await verify.UserEntityAffinities.SingleAsync()).LikeCount);
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        if (!database.StartsWith("aidomme_tracker_test_", StringComparison.Ordinal) || database.Length != 53)
            throw new InvalidOperationException("Refusing to clean up an unexpected database name.");
        await using var admin = new NpgsqlConnection(maintenance.ConnectionString);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
        command.CommandTimeout = 120;
        await command.ExecuteNonQueryAsync();
    }
}
