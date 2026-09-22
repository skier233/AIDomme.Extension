using AIDome2025.Data;
using AIDome2025.Services;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace AIDome.Tests;

/// <summary>
/// Validates that the ported EF model + migrations apply cleanly against the self-contained
/// SQLite store the extension uses, and that the HasData seeds land. This is the migration
/// smoke test for the Blazor-app → Cove-extension persistence port.
/// </summary>
public sealed class PersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aidome-test-{Guid.NewGuid():N}.db");

    private CommandCenterDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CommandCenterDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new CommandCenterDbContext(options);
    }

    [Fact]
    public async Task Migrations_Apply_And_Seed_ReferenceData()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // 30 manual + 10 toy methods are seeded via HasData.
        Assert.Equal(40, await db.StimMethods.CountAsync());
        // 14 punishment enders are seeded via HasData.
        Assert.Equal(14, await db.PunishmentEnders.CountAsync());

        var palm = await db.StimMethods.FirstOrDefaultAsync(m => m.Name == "Palm");
        Assert.NotNull(palm);
    }

    [Fact]
    public async Task InteractionManager_Creates_Session_And_Records_Edge()
    {
        await using (var db = CreateContext())
            await db.Database.MigrateAsync();

        var factory = new SingleContextFactory(_dbPath);
        var manager = new InteractionManager(factory);

        var session = await manager.StartSessionAsync(new SessionParameters(5, 5, 5, 5, 5) { IsDebugSession = true });
        Assert.True(session.Id > 0);

        var edge = await manager.RecordEdgeAsync(5, TimeSpan.FromSeconds(20));
        Assert.Equal(session.Id, edge.SessionId);

        await using var verify = CreateContext();
        Assert.Equal(1, await verify.Edges.CountAsync());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            /* best-effort temp cleanup */
        }
    }

    /// <summary>Minimal IDbContextFactory backed by a fixed SQLite file for service-level tests.</summary>
    private sealed class SingleContextFactory(string dbPath) : IDbContextFactory<CommandCenterDbContext>
    {
        public CommandCenterDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<CommandCenterDbContext>()
                .UseSqlite($"Data Source={dbPath}")
                .Options;
            return new CommandCenterDbContext(options);
        }
    }
}
