using AIDome.Data;
using AIDome.Persistence;
using Cove.Data;
using Pgvector.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace AIDome.Tests;

/// <remarks>Shares Cove's static data-extension list with other model tests, so they never run in parallel.</remarks>
[Collection("CoveModel")]
public sealed class GuidedModelTests
{
    [Fact]
    public void GuidedEntities_MapToPrefixedTables_OnCovesContext_WithUserOwnership()
    {
        using var db = new TrackerTests.TestContext(new DbContextOptionsBuilder<TrackerTests.TestContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        foreach (var type in GuidedSchema.EntityTypes)
        {
            var entity = db.Model.FindEntityType(type);
            Assert.NotNull(entity);
            Assert.StartsWith("aidomme_", entity!.GetTableName());
            Assert.NotNull(entity.FindProperty("UserId"));
        }
        Assert.Equal("aidomme_sessions", db.Model.FindEntityType(typeof(SessionRecord))!.GetTableName());
        var sql = GuidedMigrations.Load("005_guided_v2") + GuidedMigrations.Load("007_ledger");
        foreach (var table in GuidedSchema.EntityTypes.Select(x => db.Model.FindEntityType(x)!.GetTableName()))
            Assert.Contains($"CREATE TABLE {table} (", sql);
        // Cove applies migrations as raw SQL, where braces are format placeholders.
        foreach (var name in new[] { "005_guided_v2", "006_session_contracts", "007_ledger", "008_voice", "009_pace", "010_offer_values", "011_removed_definitions" })
            Assert.DoesNotContain("{", GuidedMigrations.Load(name));
    }

    [Fact]
    public void AccountQueries_AreFilteredByUser()
    {
        using var db = new TrackerTests.TestContext(new DbContextOptionsBuilder<TrackerTests.TestContext>()
            .UseNpgsql("Host=localhost;Database=unused").Options);
        var data = new GuidedDataFactory(new TestScopes(null!), 19).Join(db);
        var sql = data.Edges.ToQueryString();
        Assert.Contains("\"UserId\" = @", sql);
        Assert.Contains("19", sql);
    }

    [Fact]
    public void CovesFullModel_AcceptsTheExtension_WithTheSameColumnsTheTestsVerify()
    {
        // The PostgreSQL tests use a trimmed Cove model. Build Cove's real one with AI Domme
        // contributed, and require identical columns so those database checks carry over.
        CoveContext.SetDataExtensions([new AiDomeExtension()]);
        try
        {
            using var cove = new CoveContext(new DbContextOptionsBuilder<CoveContext>().UseNpgsql("Host=localhost;Database=unused", x => x.UseVector())
                .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, CoveModelCacheKeyFactory>().Options);
            using var trimmed = new TrackerTests.TestContext(new DbContextOptionsBuilder<TrackerTests.TestContext>()
                .UseNpgsql("Host=localhost;Database=unused").Options);
            foreach (var type in GuidedSchema.EntityTypes.Append(typeof(AIDome.Tracking.TrackerEvent)))
            {
                string Columns(DbContext db)
                {
                    var entity = db.Model.FindEntityType(type)!;
                    var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
                    return string.Join(", ", entity.GetProperties().OrderBy(x => x.Name)
                        .Select(x => $"{x.GetColumnName(table)} {x.GetColumnType()} {(x.IsNullable ? "null" : "not null")}"));
                }
                Assert.Equal(Columns(trimmed), Columns(cove));
            }
        }
        finally { CoveContext.SetDataExtensions([]); }
    }
}
