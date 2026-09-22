using System.Text.Json;
using Cove.Core.Auth;
using Cove.Core.Entities;
using Cove.Core.Interfaces;
using Cove.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Tracking;

// Scoped with the same CoveContext used by IUserEngagementService. Each mutation and all its
// weighted likes commit together. A per-account advisory lock also serializes retries/tabs.
public sealed class TrackerService(CoveContext db, ICurrentPrincipalAccessor principal,
    IAuthorizationService authorization, IUserEngagementService engagement, AIDome.Persistence.GuidedTrackerBridge? guided = null)
{
    private int UserId => principal.Current?.UserId ?? throw new UnauthorizedException();

    public async Task<TrackerSnapshot> SnapshotAsync(CancellationToken ct)
    {
        var user = UserId;
        var preferences = await db.Set<TrackerPreferences>().AsNoTracking().SingleOrDefaultAsync(x => x.UserId == user, ct)
            ?? new TrackerPreferences { UserId = user, SettingId = Guid.Empty };
        var events = db.Set<TrackerEvent>().AsNoTracking().Where(x => x.UserId == user && x.VoidedAtUtc == null);
        var last = await events.Where(x => x.Kind == "orgasm").MaxAsync(x => (DateTime?)x.OccurredAtUtc, ct);
        return new(preferences,
            await events.CountAsync(x => x.SettingId == preferences.SettingId && x.Kind == "edge", ct),
            await events.CountAsync(x => x.SettingId == preferences.SettingId && x.Kind == "orgasm", ct),
            await events.CountAsync(x => x.Kind == "edge" && (last == null || x.OccurredAtUtc > last), ct), last,
            await events.OrderByDescending(x => x.ReceivedAtUtc).ThenBy(x => x.Id).Take(10).ToListAsync(ct));
    }

    public Task<TrackerPreferences> SettingsAsync(TrackerSettingsRequest? request, bool newSetting, CancellationToken ct)
        => TransactionAsync(async () =>
        {
            if (request is not null && (request.EdgeWeight is < 0 or > 1 || request.OrgasmWeight is < 0 or > 3))
                throw new TrackerValidationException("Invalid like weights.");
            var settings = await PreferencesAsync(ct);
            if (request is not null)
            {
                settings.AutoLike = request.AutoLike;
                settings.EdgeWeight = request.EdgeWeight;
                settings.OrgasmWeight = request.OrgasmWeight;
            }
            if (newSetting) settings.SettingId = Guid.NewGuid();
            await db.SaveChangesAsync(ct);
            return settings;
        }, ct);

    public async Task<TrackerMutation> RecordAsync(RecordTrackerEvent request, CancellationToken ct)
    {
        var result = await TransactionAsync<TrackerMutation>(async () =>
        {
            var user = UserId;
            if (request.Id == Guid.Empty || request.Kind is not ("edge" or "orgasm") || request.Source is not ("button" or "hotkey"))
                throw new TrackerValidationException("Invalid tracker event.");
            var existing = await db.Set<TrackerEvent>().SingleOrDefaultAsync(x => x.Id == request.Id, ct);
            if (existing is not null)
            {
                if (existing.UserId != user) throw new TrackerValidationException("Event ID is already in use.");
                if (existing.Kind != request.Kind || existing.SettingId != request.SettingId
                    || existing.MediaKind != request.MediaKind || existing.MediaId != request.MediaId)
                    throw new TrackerValidationException("Event ID was already used for a different event.");
                return new(existing, await SnapshotAsync(ct), null);
            }
            if (request.OccurredAtUtc.Kind != DateTimeKind.Utc || request.OccurredAtUtc > DateTime.UtcNow.AddMinutes(5)
                || request.OccurredAtUtc < DateTime.UtcNow.AddDays(-1))
                throw new TrackerValidationException("Event time must be a recent UTC timestamp.");
            if ((request.MediaKind is null) != (request.MediaId is null)
                || request.MediaKind is not (null or "video" or "image") || request.MediaId is <= 0
                || request.PositionSeconds is double position && (!double.IsFinite(position) || position < 0))
                throw new TrackerValidationException("Invalid media context.");
            var settings = await PreferencesAsync(ct);
            if (request.SettingId != settings.SettingId)
                throw new TrackerValidationException("This tracking setting changed. Refresh before recording.");
            var weight = settings.AutoLike && request.MediaId.HasValue
                ? (request.Kind == "edge" ? settings.EdgeWeight : settings.OrgasmWeight) : 0;
            if (request.MediaId is int id)
                await RequireMediaAsync(request.MediaKind!, id, weight > 0, ct);
            var item = new TrackerEvent
            {
                Id = request.Id, UserId = user, SettingId = settings.SettingId, Kind = request.Kind,
                OccurredAtUtc = request.OccurredAtUtc, ReceivedAtUtc = DateTime.UtcNow, Source = request.Source,
                MediaKind = request.MediaKind, MediaId = request.MediaId, PositionSeconds = request.PositionSeconds,
                LikeWeight = weight,
            };
            db.Add(item);
            var likeIds = new List<int>();
            int? count = null;
            if (weight > 0)
            {
                await LockAffinityAsync(item.MediaKind!, item.MediaId!.Value, ct);
                var host = Host(item.MediaKind!);
                for (var i = 0; i < weight; i++)
                {
                    // Use actual stored precision and ensure a timestamp identifies only this like.
                    var at = new DateTime(DateTime.UtcNow.Ticks / 10 * 10, DateTimeKind.Utc);
                    while (await db.Interactions.AnyAsync(x => x.UserId == user && x.HostId == item.MediaId
                        && x.HostType == (InteractionHostType)(int)host && x.Kind == InteractionKind.LikeCount && x.At == at, ct))
                        at = at.AddTicks(10);
                    var before = db.ChangeTracker.Entries<Interaction>().Select(x => x.Entity).ToHashSet();
                    var snapshot = await engagement.AddHistoricalLikeAsync(host, item.MediaId.Value, at, ct)
                        ?? throw new TrackerValidationException("Media no longer exists.");
                    count = snapshot.LikeCount;
                    var created = db.ChangeTracker.Entries<Interaction>().Select(x => x.Entity).Single(x =>
                        !before.Contains(x) && x.UserId == user && x.HostId == item.MediaId && x.Kind == InteractionKind.LikeCount);
                    likeIds.Add(created.Id);
                }
            }
            item.LikeIdsJson = JsonSerializer.Serialize(likeIds);
            if (guided is not null) await guided.RecordAsync(db, item, ct, request);
            await db.SaveChangesAsync(ct);
            return new(item, await SnapshotAsync(ct), count);
        }, ct);
        if (guided is not null) await guided.DeliverAsync(result.Event, CancellationToken.None);
        return result;
    }

    public async Task<TrackerMutation> UndoAsync(Guid id, CancellationToken ct)
    {
        var result = await TransactionAsync<TrackerMutation>(async () =>
        {
            var user = UserId;
            var item = await db.Set<TrackerEvent>().SingleOrDefaultAsync(x => x.Id == id && x.UserId == user, ct)
                ?? throw new TrackerValidationException("Event not found.");
            int? count = null;
            if (item.VoidedAtUtc is null)
            {
                if (item.LikeWeight > 0 && await MediaExistsAsync(item.MediaKind!, item.MediaId!.Value, ct))
                {
                    await RequireMediaAsync(item.MediaKind!, item.MediaId!.Value, true, ct);
                    await LockAffinityAsync(item.MediaKind!, item.MediaId.Value, ct);
                    foreach (var likeId in JsonSerializer.Deserialize<List<int>>(item.LikeIdsJson)!)
                        count = (await engagement.DeleteLikeByIdAsync(Host(item.MediaKind!), item.MediaId.Value, likeId, ct))?.LikeCount;
                }
                item.VoidedAtUtc = DateTime.UtcNow;
                if (guided is not null) await guided.UndoAsync(db, item, ct);
                await db.SaveChangesAsync(ct);
            }
            return new(item, await SnapshotAsync(ct), count);
        }, ct);
        if (guided is not null) await guided.CorrectRuntimeAsync(result.Event, CancellationToken.None);
        return result;
    }

    private async Task<TrackerPreferences> PreferencesAsync(CancellationToken ct)
    {
        var user = UserId;
        var settings = await db.Set<TrackerPreferences>().SingleOrDefaultAsync(x => x.UserId == user, ct);
        if (settings is null) { settings = new() { UserId = user }; db.Add(settings); }
        return settings;
    }

    private static AffinityHostType Host(string kind) => kind == "video" ? AffinityHostType.Video : AffinityHostType.Image;

    private async Task RequireMediaAsync(string kind, int id, bool write, CancellationToken ct)
    {
        foreach (var permission in write ? new[] { "read", "write" } : new[] { "read" })
        {
            var result = await authorization.AuthorizeAsync(principal.Current, $"{kind}s.{permission}", EntityRef.Of(kind, id), ct);
            if (!result.Allowed) throw new ForbiddenException(result.Reason ?? "Media access denied.", result.MissingPermission);
        }
        if (!await MediaExistsAsync(kind, id, ct))
            throw new TrackerValidationException("Media no longer exists.");
    }

    private Task<bool> MediaExistsAsync(string kind, int id, CancellationToken ct) => kind == "video"
        ? db.Videos.AnyAsync(x => x.Id == id, ct) : db.Images.AnyAsync(x => x.Id == id, ct);

    private async Task LockAffinityAsync(string kind, int id, CancellationToken ct)
    {
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM user_entity_affinities WHERE \"UserId\" = {UserId} AND \"HostType\" = {(int)Host(kind)} AND \"HostId\" = {id} FOR UPDATE", ct);
    }

    private async Task<T> TransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var user = UserId;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({UserEngagementConcurrency.AdvisoryLockNamespace}, {user})", ct);
            var result = await operation();
            await tx.CommitAsync(ct);
            return result;
        });
    }
}

public sealed class TrackerValidationException(string message) : Exception(message);
