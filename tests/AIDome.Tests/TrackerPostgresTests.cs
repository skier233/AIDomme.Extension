using AIDome.Tracking;
using Cove.Core.Auth;
using Cove.Core.Interfaces;
using Cove.Data.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;
using AIDome.Persistence;
using AIDome2025.Data;
using AIDome2025.Services;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;

namespace AIDome.Tests;

public sealed class PostgresTrackerFactAttribute : FactAttribute
{
    public PostgresTrackerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIDOMME_TEST_POSTGRES")))
            Skip = "Set AIDOMME_TEST_POSTGRES to a local PostgreSQL maintenance connection to run isolated integration tests.";
    }
}

public sealed class LegacyDefinitionFactAttribute : FactAttribute
{
    public LegacyDefinitionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIDOMME_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AIDOMME_TEST_LEGACY_CONFIG")))
            Skip = "Set PostgreSQL and legacy definition source variables to verify a real configuration import read-only.";
    }
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
        await db.Database.ExecuteSqlRawAsync("""
            DROP TABLE aidomme_tracker_events;
            DROP TABLE aidomme_tracker_preferences;
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

    private static TrackerService Service(TrackerTests.TestContext db)
    {
        var principal = new CurrentPrincipalAccessor();
        principal.Set(new CovePrincipal { UserId = 1, Username = "isolated-test", Kind = PrincipalKind.User,
            Permissions = new HashSet<string> { "*" }, Roles = new HashSet<string>() });
        return new(db, principal, new TrackerTests.TestAuthorization(), new UserEngagementService(db, principal));
    }

    [PostgresTrackerFact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "EF1003", Justification = "DDL comes exclusively from the fixed guided model.")]
    public async Task GuidedMigration_PreservesConfiguration_IsolatesAccounts_AndConsumesTrackerOnce()
    {
        await using (var schema = Context())
        {
            var migration = GuidedSchema.CreateSql() + GuidedStore.ImportSql + GuidedRecovery.Sql;
            await schema.Database.ExecuteSqlRawAsync(migration);
            await schema.Database.ExecuteSqlRawAsync("INSERT INTO users (\"Id\") VALUES (2)");
        }
        var legacy = Path.Combine(Path.GetTempPath(), $"aidomme-definitions-{Guid.NewGuid():N}.db");
        var extensionLegacy = Path.Combine(Path.GetTempPath(), $"aidomme-extension-definitions-{Guid.NewGuid():N}.db");
        try
        {
            await using (var source = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>().UseSqlite($"Data Source={legacy};Pooling=False").Options))
            {
                await source.Database.MigrateAsync();
                (await source.StimMethods.SingleAsync(x => x.Id == 1)).TimePointMultiplier = 0.123;
                (await source.PunishmentEnders.SingleAsync(x => x.Id == 1)).Severity = 7;
                source.TuningRatings.Add(new() { CandidateKey = "method:1", Mode = "Stim", Rating = 1234, SeedRating = 1000, UpdatedAtUtc = DateTime.UtcNow });
                source.TuningComparisonHistory.Add(new() { Mode = "Stim", WinnerKey = "method:1", LoserKey = "method:2", Strength = 3, TimestampUtc = DateTime.UtcNow });
                source.Sessions.Add(new() { StartedAtUtc = DateTime.UtcNow, LastInteractionUtc = DateTime.UtcNow });
                await source.SaveChangesAsync();
            }
            await using (var extensionSource = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>().UseSqlite($"Data Source={extensionLegacy};Pooling=False").Options))
            {
                await extensionSource.Database.MigrateAsync();
                (await extensionSource.StimMethods.SingleAsync(x => x.Id == 1)).Description = "Edited extension instructions";
                await extensionSource.SaveChangesAsync();
            }
            var original = SHA256.HashData(await File.ReadAllBytesAsync(legacy));
            var extensionOriginal = SHA256.HashData(await File.ReadAllBytesAsync(extensionLegacy));
            var store = new GuidedStore(connection, extensionLegacy, legacy);
            await store.ImportAsync(default);
            await store.ImportAsync(default);
            Assert.Equal(original, SHA256.HashData(await File.ReadAllBytesAsync(legacy)));
            Assert.Equal(extensionOriginal, SHA256.HashData(await File.ReadAllBytesAsync(extensionLegacy)));
            var principal = new CurrentPrincipalAccessor();
            principal.Set(new CovePrincipal { UserId = 1, Username = "isolated", Kind = PrincipalKind.User, Permissions = new HashSet<string> { "*" }, Roles = new HashSet<string>() });
            using var runtimes = new GuidedRuntimes(store, principal, NullLoggerFactory.Instance);
            var first = runtimes.ForUser(1);
            var second = runtimes.ForUser(2);
            await using (var a = await first.Factory.CreateDbContextAsync())
            {
                Assert.Empty(await a.Sessions.ToListAsync());
                Assert.Equal(0.123, (await a.StimMethods.SingleAsync(x => x.Id == 1)).TimePointMultiplier);
                Assert.Equal("Edited extension instructions", (await a.StimMethods.SingleAsync(x => x.Id == 1)).Description);
                Assert.Equal(7, (await a.PunishmentEnders.SingleAsync(x => x.Id == 1)).Severity);
                Assert.Equal(1234, (await a.TuningRatings.SingleAsync()).Rating);
                Assert.Equal(3, (await a.TuningComparisonHistory.SingleAsync()).Strength);
                var method = await a.StimMethods.SingleAsync(x => x.Id == 1);
                method.TimePointMultiplier = 0.456;
                await a.SaveChangesAsync();
            }
            await using (var b = await second.Factory.CreateDbContextAsync())
                Assert.Equal(0.123, (await b.StimMethods.SingleAsync(x => x.Id == 1)).TimePointMultiplier);
            await using (var a = await first.Factory.CreateDbContextAsync())
                Assert.Equal(0.456, (await a.StimMethods.SingleAsync(x => x.Id == 1)).TimePointMultiplier);
            var configuration = new GuidedConfiguration(runtimes);
            await configuration.ResetAsync<StimMethod>(1, default);
            await using (var a = await first.Factory.CreateDbContextAsync())
                Assert.Equal(0.123, (await a.StimMethods.SingleAsync(x => x.Id == 1)).TimePointMultiplier);

            await first.Commands.StartSessionAsync(new SessionParameters(3, 3, 3, 3, 3));
            Assert.True(first.Online.HasActiveSession);
            Assert.False(second.Online.HasActiveSession);
            var sessionId = first.Online.GetCurrentState().SessionId;
            await using (var a = await first.Factory.CreateDbContextAsync())
            {
                (await a.Sessions.SingleAsync(x => x.Id == sessionId)).LastInteractionUtc = DateTime.UtcNow.AddHours(-1);
                await a.SaveChangesAsync();
            }
            await first.Commands.GenerateDebugHomeworkAsync(new SessionParameters(8, 3, 3, 3, 3) { IsDebugSession = true });
            Assert.True((await first.Commands.GetSessionStatusAsync()).IsActive);
            Assert.Equal(sessionId, await first.Interaction.GetCurrentSessionId());
            await using (var b = await second.Factory.CreateDbContextAsync())
            {
                Assert.Empty(await b.Sessions.ToListAsync());
                b.Edges.Add(new() { SessionId = sessionId, StimMethodId = 1, TimestampUtc = DateTime.UtcNow });
                await Assert.ThrowsAsync<DbUpdateException>(() => b.SaveChangesAsync());
            }
            var bridge = new GuidedTrackerBridge(runtimes, NullLogger<GuidedTrackerBridge>.Instance);
            await using var trackerDb = Context();
            var tracking = new AIDome.Tracking.TrackerService(trackerDb, principal, new TrackerTests.TestAuthorization(), new UserEngagementService(trackerDb, principal), bridge);
            var prefs = await tracking.SettingsAsync(new(false, 0, 1), false, default);
            var request = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "edge", DateTime.UtcNow, "hotkey");
            await tracking.RecordAsync(request, default);
            await tracking.RecordAsync(request, default);
            var metrics = await first.Factory.ReadTrackerMetricsAsync(DateTime.UtcNow, default);
            Assert.Equal(1, metrics.EdgesSinceLastOrgasm);
            Assert.Null(metrics.LastOrgasmAtUtc);
            Assert.Equal(1, first.Online.GetCurrentState().EdgesThisSession);
            await using (var a = await first.Factory.CreateDbContextAsync())
            {
                Assert.Single(await a.Edges.ToListAsync());
                Assert.Single(await a.TrackerReceipts.ToListAsync());
            }
            await tracking.UndoAsync(request.Id, default);
            Assert.False(first.Online.HasActiveSession);
            await using (var a = await first.Factory.CreateDbContextAsync())
            {
                Assert.Empty(await a.Edges.ToListAsync());
                Assert.False((await a.Sessions.SingleAsync(x => x.Id == sessionId)).SuspectedUnauthorizedOrgasm);
            }
            await using (var b = await second.Factory.CreateDbContextAsync()) Assert.Empty(await b.Edges.ToListAsync());
            var passive = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "orgasm", DateTime.UtcNow, "button");
            await tracking.RecordAsync(passive, default);
            metrics = await first.Factory.ReadTrackerMetricsAsync(DateTime.UtcNow, default);
            Assert.NotNull(metrics.LastOrgasmAtUtc);
            Assert.Equal(0, metrics.EdgesSinceLastOrgasm);
            await using (var a = await first.Factory.CreateDbContextAsync()) Assert.Empty(await a.Orgasms.ToListAsync());
            await first.Commands.StartSessionAsync(new SessionParameters(3, 3, 3, 3, 3) { IsDebugSession = true });
            var staleSession = first.Online.GetCurrentState().SessionId;
            await using (var a = await first.Factory.CreateDbContextAsync())
                await a.Sessions.Where(x => x.Id == staleSession).ExecuteDeleteAsync();
            await tracking.SettingsAsync(new(true, 1, 3), false, default);
            var failed = new AIDome.Tracking.RecordTrackerEvent(Guid.NewGuid(), prefs.SettingId, "edge", DateTime.UtcNow, "button", "video", 1);
            await Assert.ThrowsAsync<AIDome.Tracking.TrackerValidationException>(() => tracking.RecordAsync(failed, default));
            trackerDb.ChangeTracker.Clear();
            Assert.False(await trackerDb.Set<AIDome.Tracking.TrackerEvent>().AnyAsync(x => x.Id == failed.Id));
            Assert.Empty(await trackerDb.Interactions.ToListAsync());
            Assert.Empty(await trackerDb.UserEntityAffinities.ToListAsync());
            Assert.Equal(0, first.Online.GetCurrentState().EdgesThisSession);
            first.Online.EndSession();
            await trackerDb.Database.ExecuteSqlRawAsync("DELETE FROM users WHERE \"Id\" = 1");
            Assert.Equal(0, await trackerDb.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM aidomme_guided_sessionrecord WHERE \"OwnerId\" = 1").SingleAsync());
        }
        finally
        {
            if (File.Exists(legacy)) File.Delete(legacy);
            if (File.Exists(extensionLegacy)) File.Delete(extensionLegacy);
        }
    }

    [LegacyDefinitionFact]
    public async Task OriginalConfiguration_ImportsEveryDefinitionAndCalibrationValue_WithoutUsageOrSourceChanges()
    {
        await using var tracker = Context();
        var migration = GuidedSchema.CreateSql() + GuidedStore.ImportSql + GuidedRecovery.Sql;
        await tracker.Database.ExecuteSqlRawAsync(migration);
        var path = Environment.GetEnvironmentVariable("AIDOMME_TEST_LEGACY_CONFIG")!;
        var hash = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var store = new GuidedStore(connection, Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.db"), path);
        await store.ImportAsync(default);
        await store.ImportAsync(default);
        await using var source = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>()
            .UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString()).Options);
        await using var effective = await store.ForUser(1).CreateDbContextAsync();
        await AssertDefinitionParityAsync<StimMethod>(source, effective);
        await AssertDefinitionParityAsync<PunishmentEnder>(source, effective);
        await AssertDefinitionParityAsync<TuningRatingEntry>(source, effective);
        await AssertDefinitionParityAsync<TuningComparisonEntry>(source, effective);
        Assert.Equal(await source.TuningRatings.CountAsync(), await effective.TuningRatings.CountAsync());
        Assert.Equal(await source.TuningComparisonHistory.CountAsync(), await effective.TuningComparisonHistory.CountAsync());
        Assert.Empty(await effective.Sessions.ToListAsync());
        Assert.Empty(await effective.Edges.ToListAsync());
        Assert.Empty(await effective.Orgasms.ToListAsync());
        Assert.Empty(await effective.Punishments.ToListAsync());
        Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(path)));
    }

    private static async Task AssertDefinitionParityAsync<T>(CommandCenterDbContext source, CommandCenterDbContext target) where T : class
    {
        var rows = await target.Set<T>().ToListAsync();
        foreach (var original in await source.Set<T>().AsNoTracking().ToListAsync())
        {
            var id = typeof(T).GetProperty("Id")!.GetValue(original);
            var copy = Assert.Single(rows, x => Equals(typeof(T).GetProperty("Id")!.GetValue(x), id));
            foreach (var property in source.Model.FindEntityType(typeof(T))!.GetProperties())
            {
                var expected = property.PropertyInfo!.GetValue(original);
                var actual = property.PropertyInfo.GetValue(copy);
                if (expected is DateTime time) expected = new DateTime(time.Ticks / 10 * 10, DateTimeKind.Utc);
                Assert.Equal(expected, actual);
            }
        }
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
