using System.Reflection;
using AIDomme.Tracking;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Cove.Data.Configuration;
using Cove.Data.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIDomme.Tests;

public sealed class TrackerTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly TestContext db;
    private readonly CurrentPrincipalAccessor principal = new();
    private readonly TestAuthorization authorization = new();
    private readonly EngagementProxy proxy;
    private readonly TrackerService service;

    public TrackerTests()
    {
        connection.Open();
        db = new(new DbContextOptionsBuilder<TestContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("INSERT INTO Videos (Id) VALUES (1)");
        db.Database.ExecuteSqlRaw("INSERT INTO Images (Id) VALUES (1)");
        var engagement = DispatchProxy.Create<IUserEngagementService, EngagementProxy>();
        proxy = (EngagementProxy)engagement;
        proxy.Db = db;
        SetUser(1);
        proxy.Principal = principal;
        service = new(db, principal, authorization, engagement);
    }

    private void SetUser(int user) => principal.Set(new CovePrincipal
    {
        UserId = user, Username = "test", Kind = PrincipalKind.User,
        Roles = new HashSet<string>(), Permissions = new HashSet<string> { "*" },
    });

    [Fact]
    public async Task UndoDoesNotDeleteAReplacementLikeWithTheSameTimestamp()
    {
        var realEngagement = new UserEngagementService(db, principal);
        var tracker = new TrackerService(db, principal, authorization, realEngagement);
        var settings = await tracker.SettingsAsync(new(true, 1, 1), false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), settings.SettingId, "edge", DateTime.UtcNow, "button", "video", 1);
        await tracker.RecordAsync(request, default);
        var original = await db.Interactions.AsNoTracking().SingleAsync();
        await realEngagement.DeleteLikeAtAsync(AffinityHostType.Video, 1, original.At);
        await realEngagement.AddHistoricalLikeAsync(AffinityHostType.Video, 1, original.At);
        await tracker.UndoAsync(request.Id, default);
        Assert.Equal(1, (await realEngagement.GetSnapshotAsync(AffinityHostType.Video, 1))!.LikeCount);
        Assert.Equal(1, await db.Interactions.CountAsync());
    }

    [Fact]
    public async Task RealCoveImageLikes_ApplyEdgeAndOrgasmWeights()
    {
        var realEngagement = new UserEngagementService(db, principal);
        var tracker = new TrackerService(db, principal, authorization, realEngagement);
        var settings = await tracker.SettingsAsync(new(true, 1, 2), false, default);
        await tracker.RecordAsync(new(Guid.NewGuid(), settings.SettingId, "edge", DateTime.UtcNow, "button", "image", 1), default);
        await tracker.RecordAsync(new(Guid.NewGuid(), settings.SettingId, "orgasm", DateTime.UtcNow, "button", "image", 1), default);
        Assert.Equal(3, (await realEngagement.GetSnapshotAsync(AffinityHostType.Image, 1))!.LikeCount);
        SetUser(2);
        Assert.Equal(0, (await realEngagement.GetSnapshotAsync(AffinityHostType.Image, 1))!.LikeCount);
    }

    [Fact]
    public async Task RealCoveEngagementService_UpdatesCounterAndHistoryTogether()
    {
        var realEngagement = new UserEngagementService(db, principal);
        var tracker = new TrackerService(db, principal, authorization, realEngagement);
        var settings = await tracker.SettingsAsync(new(true, 1, 3), false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), settings.SettingId, "orgasm", DateTime.UtcNow, "hotkey", "video", 1);
        await tracker.RecordAsync(request, default);
        await tracker.RecordAsync(request, default);
        Assert.Equal(3, (await realEngagement.GetSnapshotAsync(AffinityHostType.Video, 1))!.LikeCount);
        await realEngagement.IncrementVideoLikeAsync(1);
        await tracker.UndoAsync(request.Id, default);
        Assert.Equal(1, (await realEngagement.GetSnapshotAsync(AffinityHostType.Video, 1))!.LikeCount);
        Assert.Equal(1, await db.Interactions.CountAsync());
    }

    [Fact]
    public async Task Tracking_WithoutGuidance_IsIdempotentAndUserScoped()
    {
        var preferences = await service.SettingsAsync(null, false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), preferences.SettingId, "edge", DateTime.UtcNow, "hotkey");
        await service.RecordAsync(request, default);
        await service.RecordAsync(request, default);
        Assert.Equal(1, (await service.SnapshotAsync(default)).Edges);
        SetUser(2);
        Assert.Equal(0, (await service.SnapshotAsync(default)).Edges);
        await Assert.ThrowsAsync<TrackerValidationException>(() => service.UndoAsync(request.Id, default));
    }

    [Fact]
    public async Task WeightedLikes_AndUndo_UseOriginalWeightAndExactEffects()
    {
        var settings = await service.SettingsAsync(new(true, 1, 3), false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), settings.SettingId, "orgasm", DateTime.UtcNow, "button", "video", 1, 42);
        await service.RecordAsync(request, default);
        await service.RecordAsync(request, default);
        Assert.Equal(3, await db.Interactions.CountAsync());
        // An unrelated manual like must survive undo.
        db.Interactions.Add(new() { UserId = 1, HostId = 1, HostType = InteractionHostType.Video,
            Kind = InteractionKind.LikeCount, At = DateTime.UtcNow.AddMinutes(1) });
        await db.SaveChangesAsync();
        await service.SettingsAsync(new(false, 0, 1), false, default);
        await service.UndoAsync(request.Id, default);
        await service.UndoAsync(request.Id, default);
        Assert.Equal(1, await db.Interactions.CountAsync());
        Assert.Equal(0, (await service.SnapshotAsync(default)).Orgasms);
    }

    [Fact]
    public async Task FailurePartwayThroughLikes_RollsBackEventAndEveryEffect()
    {
        var settings = await service.SettingsAsync(new(true, 1, 3), false, default);
        var request = new RecordTrackerEvent(Guid.NewGuid(), settings.SettingId, "orgasm", DateTime.UtcNow, "button", "video", 1);
        proxy.FailAfterSave = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.RecordAsync(request, default));
        Assert.Equal(0, await db.Set<TrackerEvent>().AsNoTracking().CountAsync());
        Assert.Equal(0, await db.Interactions.AsNoTracking().CountAsync());
        proxy.FailAfterSave = 0;
        await service.RecordAsync(request, default);
        Assert.Equal(3, await db.Interactions.CountAsync());
    }

    [Fact]
    public async Task Metrics_AreRecomputedAfterUndo_AndSettingsKeepHistory()
    {
        var settings = await service.SettingsAsync(null, false, default);
        var now = DateTime.UtcNow;
        await service.RecordAsync(new(Guid.NewGuid(), settings.SettingId, "edge", now.AddMinutes(-3), "button"), default);
        var orgasm = Guid.NewGuid();
        await service.RecordAsync(new(orgasm, settings.SettingId, "orgasm", now.AddMinutes(-2), "button"), default);
        await service.RecordAsync(new(Guid.NewGuid(), settings.SettingId, "edge", now.AddMinutes(-1), "button"), default);
        Assert.Equal(1, (await service.SnapshotAsync(default)).EdgesSinceLastOrgasm);
        await service.UndoAsync(orgasm, default);
        Assert.Equal(2, (await service.SnapshotAsync(default)).EdgesSinceLastOrgasm);
        Assert.Null((await service.SnapshotAsync(default)).LastOrgasmAtUtc);
        await service.SettingsAsync(null, true, default);
        var snapshot = await service.SnapshotAsync(default);
        Assert.Equal(0, snapshot.Edges);
        Assert.Equal(2, snapshot.EdgesSinceLastOrgasm);
    }

    [Fact]
    public async Task ForbiddenMediaAndInvalidWeights_DoNotRecordAnything()
    {
        await Assert.ThrowsAsync<TrackerValidationException>(() => service.SettingsAsync(new(true, 2, 3), false, default));
        var settings = await service.SettingsAsync(new(true, 1, 3), false, default);
        authorization.Allow = false;
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RecordAsync(new(Guid.NewGuid(), settings.SettingId,
            "edge", DateTime.UtcNow, "button", "video", 1), default));
        Assert.Equal(0, await db.Set<TrackerEvent>().CountAsync());
    }

    public void Dispose() { principal.Set(null); db.Dispose(); connection.Dispose(); }

    // Small relational fixture, not Cove's full media schema. It verifies transaction boundaries
    // against a service adapter that, like the real engagement service, saves on each like.
    public sealed class TestContext(DbContextOptions<TestContext> options) : CoveContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            TrackerSchema.Configure(model);
            AIDomme.Persistence.GuidedSchema.Configure(model);
            var video = model.Entity<Video>();
            foreach (var property in typeof(Video).GetProperties().Where(x => x.Name != "Id")) video.Ignore(property.Name);
            video.HasKey(x => x.Id);
            var image = model.Entity<Image>();
            foreach (var property in typeof(Image).GetProperties().Where(x => x.Name != "Id")) image.Ignore(property.Name);
            image.HasKey(x => x.Id);
            model.Entity<Interaction>().Ignore(x => x.Meta).HasKey(x => x.Id);
            model.ApplyConfiguration(new UserEntityAffinityConfiguration());
            model.Entity<Rating>().HasKey(x => x.Id);
            var allowed = new HashSet<Type>(AIDomme.Persistence.GuidedSchema.EntityTypes) { typeof(TrackerEvent), typeof(TrackerPreferences), typeof(Video), typeof(Image), typeof(Interaction), typeof(UserEntityAffinity), typeof(Rating) };
            foreach (var type in model.Model.GetEntityTypes().Select(x => x.ClrType).Where(x => !allowed.Contains(x)).ToArray())
                model.Ignore(type);
        }
        public override Task<int> SaveChangesAsync(CancellationToken ct = default) => base.SaveChangesAsync(true, ct);
    }

    public class EngagementProxy : DispatchProxy
    {
        public TestContext Db = null!;
        public CurrentPrincipalAccessor Principal = null!;
        public int FailAfterSave;
        private int saves;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            return ApplyAsync(method!.Name, args!);
        }
        private async Task<UserEngagementSnapshot?> ApplyAsync(string method, object?[] args)
        {
            var host = (AffinityHostType)args[0]!;
            var id = (int)args[1]!;
            var at = args[2] is DateTime date ? date : DateTime.MinValue;
            var user = Principal.Current!.UserId!.Value;
            if (method == "AddHistoricalLikeAsync")
                Db.Interactions.Add(new() { UserId = user, HostId = id, HostType = (InteractionHostType)(int)host,
                    Kind = InteractionKind.LikeCount, At = at });
            else if (method == "DeleteLikeByIdAsync")
            {
                var likeId = (int)args[2]!;
                var item = await Db.Interactions.SingleOrDefaultAsync(x => x.UserId == user && x.HostId == id && x.Id == likeId);
                if (item is not null) Db.Remove(item);
            }
            else throw new NotSupportedException(method);
            await Db.SaveChangesAsync();
            if (FailAfterSave > 0 && ++saves == FailAfterSave) throw new InvalidOperationException("Injected like failure.");
            return new(false, null, 0, 0, 0, null, await Db.Interactions.CountAsync(), 0, 0, 0);
        }
    }

    public sealed class TestAuthorization : IAuthorizationService
    {
        public bool Allow = true;
        public AuthorizationResult Authorize(CovePrincipal? p, string permission, EntityRef? entity = null)
            => Allow ? AuthorizationResult.Allow() : AuthorizationResult.Deny("Denied");
        public Task<AuthorizationResult> AuthorizeAsync(CovePrincipal? p, string permission, EntityRef? entity, CancellationToken ct)
            => Task.FromResult(Authorize(p, permission, entity));
        public void Require(CovePrincipal? p, string permission, EntityRef? entity = null) { }
        public bool Has(CovePrincipal? p, string permission) => Allow;
    }
}
