using AIDome;
using AIDome.Tracking;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace AIDome.Tests;

public sealed class TrackerModelTests
{
    [Fact]
    public void CovePostgresModel_IncludesTrackerAndExactReceiptColumn()
    {
        CoveContext.SetDataExtensions([new AiDomeExtension()]);
        try
        {
            var options = new DbContextOptionsBuilder<CoveContext>()
                .UseNpgsql("Host=localhost;Database=tracker_model_only;Username=unused;Password=unused", x => x.UseVector())
                .ReplaceService<IModelCacheKeyFactory, CoveModelCacheKeyFactory>().Options;
            using var db = new CoveContext(options);
            Assert.Equal("aidomme_tracker_events", db.Model.FindEntityType(typeof(TrackerEvent))!.GetTableName());
            Assert.Equal("aidomme_tracker_preferences", db.Model.FindEntityType(typeof(TrackerPreferences))!.GetTableName());
            var script = db.Database.GenerateCreateScript(); // Does not connect to a database.
            Assert.Contains("\"LikeIdsJson\" text NOT NULL", script);
            Assert.Contains("\"OccurredAtUtc\" timestamp with time zone NOT NULL", script);
        }
        finally { CoveContext.SetDataExtensions([]); }
    }
}
