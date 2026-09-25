using AIDome.Data;
using Cove.Plugins;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AIDome.Persistence;

/// <summary>
/// One account's view of AI Domme's tables on Cove's own context. Every query property is already
/// filtered to <see cref="UserId"/>, and <see cref="Add{T}"/> stamps ownership, so guided services
/// never see or write another account's rows.
/// </summary>
public sealed class GuidedData : IAsyncDisposable, IDisposable
{
    private readonly IServiceScope? scope;

    internal GuidedData(DbContext db, int userId, IServiceScope? scope)
    {
        Db = db;
        UserId = userId;
        this.scope = scope;
    }

    /// <summary>Cove's context. Use the filtered properties below for AI Domme rows.</summary>
    public DbContext Db { get; }
    public int UserId { get; }
    public DatabaseFacade Database => Db.Database;

    public IQueryable<SessionRecord> Sessions => Owned<SessionRecord>();
    public IQueryable<EdgeEntry> Edges => Owned<EdgeEntry>();
    public IQueryable<OrgasmEntry> Orgasms => Owned<OrgasmEntry>();
    public IQueryable<CumRequest> CumRequests => Owned<CumRequest>();
    public IQueryable<InstructionStep> InstructionSteps => Owned<InstructionStep>();
    public IQueryable<InfractionEntry> Infractions => Owned<InfractionEntry>();
    public IQueryable<GameChallenge> GameChallenges => Owned<GameChallenge>();
    public IQueryable<GuidedEventReceipt> TrackerReceipts => Owned<GuidedEventReceipt>();
    public IQueryable<OfferRecord> Offers => Owned<OfferRecord>();
    public IQueryable<CommitmentRecord> Commitments => Owned<CommitmentRecord>();
    public IQueryable<FollowUpRecord> FollowUps => Owned<FollowUpRecord>();
    public IQueryable<LedgerEntry> Ledger => Owned<LedgerEntry>();

    /// <summary>Shared calibration plus this account's own.</summary>
    public IQueryable<TuningRatingEntry> TuningRatings
        => Db.Set<TuningRatingEntry>().Where(x => x.UserId == null || x.UserId == UserId);
    public IQueryable<TuningComparisonEntry> TuningComparisons
        => Db.Set<TuningComparisonEntry>().Where(x => x.UserId == null || x.UserId == UserId);

    public IQueryable<T> Owned<T>() where T : class, IUserOwned => Db.Set<T>().Where(x => x.UserId == UserId);

    public T Add<T>(T entity) where T : class, IUserOwned
    {
        entity.UserId = UserId;
        Db.Add(entity);
        return entity;
    }

    public void AddRange<T>(IEnumerable<T> entities) where T : class, IUserOwned
    {
        foreach (var entity in entities) Add(entity);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Db.SaveChangesAsync(ct);

    /// <summary>
    /// Runs <paramref name="operation"/> in one transaction inside Cove's retrying execution strategy.
    /// The operation may be re-run after a transient failure, so it must start from the database.
    /// </summary>
    public Task<T> InTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct,
        System.Data.IsolationLevel isolation = System.Data.IsolationLevel.ReadCommitted)
        => Db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            Db.ChangeTracker.Clear();
            await using var transaction = await Db.Database.BeginTransactionAsync(isolation, ct);
            var result = await operation();
            await transaction.CommitAsync(ct);
            return result;
        });
    public int SaveChanges() => Db.SaveChanges();

    /// <summary>The methods this account uses: shared defaults with personal overrides applied.</summary>
    /// <summary>The methods in this account's library (not the ones the user removed).</summary>
    public async Task<List<StimMethod>> MethodsAsync(CancellationToken ct = default)
        => (await Definitions.EffectiveAsync(Db.Set<StimMethod>(), UserId, ct)).Where(x => !x.Removed).ToList();
    public List<StimMethod> Methods() => Definitions.Effective(Db.Set<StimMethod>(), UserId).Where(x => !x.Removed).ToList();
    /// <summary>The punishment endings in this account's library (not the ones the user removed).</summary>
    public async Task<List<PunishmentEnder>> EndersAsync(CancellationToken ct = default)
        => (await Definitions.EffectiveAsync(Db.Set<PunishmentEnder>(), UserId, ct)).Where(x => !x.Removed).ToList();
    public Task<List<OfferDefinition>> OfferDefinitionsAsync(CancellationToken ct = default) => Definitions.EffectiveAsync(Db.Set<OfferDefinition>(), UserId, ct);

    public void Dispose() => scope?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (scope is IAsyncDisposable asyncScope) await asyncScope.DisposeAsync();
        else scope?.Dispose();
    }
}

/// <summary>Opens <see cref="GuidedData"/> for one account, each in its own extension-owned scope.</summary>
public sealed class GuidedDataFactory(IExtensionServiceScopeFactory scopes, int userId)
{
    public int UserId => userId;

    public Task<GuidedData> OpenAsync(CancellationToken ct = default) => Task.FromResult(Open());

    public GuidedData Open()
    {
        var scope = scopes.CreateScope();
        try { return new(scope.ServiceProvider.GetRequiredService<DbContext>(), userId, scope); }
        catch { scope.Dispose(); throw; }
    }

    /// <summary>Uses a caller's context, and so its open transaction (the tracker's event commit).</summary>
    public GuidedData Join(DbContext db) => new(db, userId, null);

    /// <summary>Passive tracking counts too: permission decisions read the full tracker history.</summary>
    public async Task<TrackerDecisionMetrics> ReadTrackerMetricsAsync(DateTime nowUtc, CancellationToken ct)
    {
        await using var data = Open();
        var day = nowUtc.Date;
        var end = day.AddDays(1);
        return await data.Database.SqlQuery<TrackerDecisionMetrics>($"""
            WITH facts AS (SELECT "Kind", "OccurredAtUtc" FROM aidomme_tracker_events WHERE "UserId" = {userId} AND "VoidedAtUtc" IS NULL),
              latest AS (SELECT MAX("OccurredAtUtc") AS last FROM facts WHERE "Kind" = 'orgasm')
            SELECT (SELECT last FROM latest) AS "LastOrgasmAtUtc",
              COUNT(*) FILTER (WHERE "Kind" = 'edge' AND ((SELECT last FROM latest) IS NULL OR "OccurredAtUtc" > (SELECT last FROM latest)))::integer AS "EdgesSinceLastOrgasm",
              COUNT(*) FILTER (WHERE "Kind" = 'edge' AND "OccurredAtUtc" >= {day} AND "OccurredAtUtc" < {end})::integer AS "EdgesToday"
            FROM facts
            """).SingleAsync(ct);
    }
}

public sealed class TrackerDecisionMetrics
{
    public DateTime? LastOrgasmAtUtc { get; set; }
    public int EdgesSinceLastOrgasm { get; set; }
    public int EdgesToday { get; set; }
}

/// <summary>Resolves shared definitions, personal overrides and personal definitions into one set.</summary>
public static class Definitions
{
    public static async Task<List<T>> EffectiveAsync<T>(IQueryable<T> set, int userId, CancellationToken ct) where T : class, IDefinition<T>
        => Resolve(await set.AsNoTracking().Where(x => x.UserId == null || x.UserId == userId).ToListAsync(ct));

    public static List<T> Effective<T>(IQueryable<T> set, int userId) where T : class, IDefinition<T>
        => Resolve(set.AsNoTracking().Where(x => x.UserId == null || x.UserId == userId).ToList());

    /// <summary>An account's effective definitions from its rows and the shared ones: overrides in place of defaults, then its own.</summary>
    public static List<T> Resolve<T>(List<T> rows) where T : class, IDefinition<T>
    {
        var overrides = rows.Where(x => x.UserId is not null && x.BaseId is int).ToDictionary(x => x.BaseId!.Value);
        return rows.Where(x => x.UserId is null)
            .Select(shared => overrides.TryGetValue(shared.Id, out var own) ? own.AsDefinition(shared.Id) : shared)
            .Concat(rows.Where(x => x.UserId is not null && x.BaseId is null))
            .OrderBy(x => x.Id)
            .ToList();
    }
}
