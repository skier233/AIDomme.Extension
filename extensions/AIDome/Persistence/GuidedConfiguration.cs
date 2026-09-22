using System.ComponentModel.DataAnnotations;
using AIDome.Data;
using AIDome.Services;
using AIDome.Tracking;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record DefinitionView<T>(T Value, bool IsOverride, T? SharedDefault);

/// <summary>
/// The account's method and punishment-ending library: shared defaults, personal overrides of them,
/// and the calibration used to tune method multipliers.
/// </summary>
public sealed class GuidedConfiguration(GuidedRuntimes runtimes)
{
    public async Task<object> SnapshotAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        return new
        {
            Methods = await ViewsAsync(db.Db.Set<StimMethod>(), db.UserId, ct),
            Enders = await ViewsAsync(db.Db.Set<PunishmentEnder>(), db.UserId, ct),
            Ratings = (await db.TuningRatings.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => new DefinitionView<TuningRatingEntry>(x, x.UserId is not null, null)),
            Comparisons = (await db.TuningComparisons.AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct))
                .Select(x => new DefinitionView<TuningComparisonEntry>(x, x.UserId is not null, null)),
        };
    }

    private static async Task<List<DefinitionView<T>>> ViewsAsync<T>(IQueryable<T> set, int userId, CancellationToken ct)
        where T : class, IDefinition<T>
    {
        var rows = await set.AsNoTracking().Where(x => x.UserId == null || x.UserId == userId).ToListAsync(ct);
        var shared = rows.Where(x => x.UserId is null).ToDictionary(x => x.Id);
        var overridden = rows.Where(x => x.BaseId is int).Select(x => x.BaseId!.Value).ToHashSet();
        var effective = await Definitions.EffectiveAsync(set, userId, ct);
        return effective.Select(x => new DefinitionView<T>(x, overridden.Contains(x.Id), shared.GetValueOrDefault(x.Id))).ToList();
    }

    public async Task<T> SaveAsync<T>(int id, T value, CancellationToken ct) where T : class, IDefinition<T>
    {
        Validate(value);
        if (id != value.Id) throw new TrackerValidationException("Definition ID cannot change.");
        await using var db = runtimes.Current.Factory.Open();
        // Saving even an identical value is an explicit personal override.
        var own = await OverrideAsync<T>(db, id, ct);
        CopyScalars(db, own, value);
        await db.SaveChangesAsync(ct);
        return own.AsDefinition(id);
    }

    public async Task<T> ResetAsync<T>(int id, CancellationToken ct) where T : class, IDefinition<T>
    {
        await using var db = runtimes.Current.Factory.Open();
        var shared = await db.Db.Set<T>().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == null, ct)
            ?? throw new TrackerValidationException("No shared default exists for this definition.");
        await db.Db.Set<T>().Where(x => x.UserId == db.UserId && x.BaseId == id).ExecuteDeleteAsync(ct);
        return shared;
    }

    public async Task<object> OptimizeAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        var methods = await db.MethodsAsync(ct);
        StimMultiplierOptimizer.OptimizationResult result;
        try { result = StimMultiplierOptimizer.Optimize(methods, await db.TuningComparisons.AsNoTracking().ToListAsync(ct)); }
        catch (InvalidOperationException ex) { throw new TrackerValidationException(ex.Message); }
        foreach (var method in methods)
        {
            var multiplier = result.Multipliers[method.Id];
            // Tuned multipliers are personal: they become (or update) this account's override.
            var own = await OverrideAsync<StimMethod>(db, method.Id, ct);
            own.TimePointMultiplier = multiplier.TimePointsPerSecond;
            own.EdgePointMultiplier = multiplier.EdgePointsPerEdge;
        }
        await db.SaveChangesAsync(ct);
        return new
        {
            result.WinningAlgorithm, result.ValidationLogLoss, result.ValidationAccuracy, result.Trials,
            Multipliers = result.Multipliers.ToDictionary(x => x.Key, x => new { TimePointsPerSecond = x.Value.TimePointsPerSecond, EdgePointsPerEdge = x.Value.EdgePointsPerEdge }),
        };
    }

    /// <summary>
    /// The tracked row this account edits for definition <paramref name="id"/>: its existing override,
    /// its own personal definition, or a new override initialized from the shared default.
    /// </summary>
    private static async Task<T> OverrideAsync<T>(GuidedData db, int id, CancellationToken ct) where T : class, IDefinition<T>
    {
        var set = db.Db.Set<T>();
        var own = await set.SingleOrDefaultAsync(x => x.UserId == db.UserId && (x.BaseId == id || (x.BaseId == null && x.Id == id)), ct);
        if (own is not null) return own;
        var shared = await set.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == null, ct)
            ?? throw new TrackerValidationException("Definition not found.");
        own = shared.AsDefinition(0);
        own.UserId = db.UserId;
        own.BaseId = id;
        set.Add(own);
        return own;
    }

    private static void CopyScalars<T>(GuidedData db, T target, T source) where T : class
    {
        var entry = db.Db.Entry(target);
        foreach (var property in entry.Metadata.GetProperties().Where(x => x.PropertyInfo is not null && x.Name is not ("Id" or "UserId" or "BaseId")))
            entry.Property(property.Name).CurrentValue = property.PropertyInfo!.GetValue(source);
    }

    private static void Validate<T>(T value) where T : class
    {
        var failures = new List<ValidationResult>();
        if (!Validator.TryValidateObject(value, new ValidationContext(value), failures, true))
            throw new TrackerValidationException(string.Join(" ", failures.Select(x => x.ErrorMessage)));
        if (value is StimMethod m && (string.IsNullOrWhiteSpace(m.Name) || m.Description is null || m.Intensity is < 0 or > 10 || m.Tease is < 0 or > 10
            || m.TortLevel is < 0 or > 10 || m.Closeness is < 0 or > 10 || !Enum.IsDefined(m.ToyCategory)
            || !double.IsFinite(m.TimePointMultiplier) || m.TimePointMultiplier < 0 || !double.IsFinite(m.EdgePointMultiplier) || m.EdgePointMultiplier < 0))
            throw new TrackerValidationException("Method values must use valid scales and nonnegative finite multipliers.");
        if (value is PunishmentEnder p && (string.IsNullOrWhiteSpace(p.Name) || p.InstructionText is null || p.AnnouncementText is null || p.Severity is < 0 or > 10
            || p.Anticipation is < 0 or > 10 || p.Humiliation is < 0 or > 10))
            throw new TrackerValidationException("Ender values must use valid scales.");
    }
}
