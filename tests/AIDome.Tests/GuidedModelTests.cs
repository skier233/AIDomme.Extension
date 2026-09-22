using AIDome.Persistence;
using AIDome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace AIDome.Tests;

public sealed class GuidedModelTests
{
    [Fact]
    public void PostgreSqlSchema_HasAccountKeysAndForeignKeys_AndSharedSeeds()
    {
        var sql = GuidedSchema.CreateSql();
        Assert.Contains("aidomme_guided_sessionrecord", sql);
        Assert.Contains("PRIMARY KEY (\"OwnerId\", \"Id\")", sql);
        Assert.Contains("FOREIGN KEY (\"OwnerId\", \"SessionId\")", sql);
        Assert.Contains("aidomme_guided_stimmethod", sql);
        using var db = new CommandCenterDbContext(new DbContextOptionsBuilder<CommandCenterDbContext>().UseNpgsql("Host=localhost;Database=unused")
            .ReplaceService<IModelCacheKeyFactory, GuidedModelCacheKeyFactory>().Options, 19);
        Assert.Contains("19", db.Edges.ToQueryString());
    }
}
