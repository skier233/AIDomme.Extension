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
            Offers = await ViewsAsync(db.Db.Set<OfferDefinition>(), db.UserId, ct),
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

    /// <summary>A new definition of the user's own (not an override of a shared one).</summary>
    public async Task<T> CreateAsync<T>(T value, CancellationToken ct) where T : class, IDefinition<T>
    {
        Validate(value);
        await using var db = runtimes.Current.Factory.Open();
        value.Id = 0;
        value.UserId = db.UserId;
        value.BaseId = null;
        db.Db.Set<T>().Add(value);
        await db.SaveChangesAsync(ct);
        return value.AsDefinition(value.Id);
    }

    /// <summary>
    /// Takes a method or ending out of this account's library, or puts it back. Nothing is deleted:
    /// history still points at it. A shared default is removed through the account's override.
    /// </summary>
    public async Task<T> SetRemovedAsync<T>(int id, bool removed, CancellationToken ct) where T : class, IDefinition<T>, IRemovable
    {
        await using var db = runtimes.Current.Factory.Open();
        var own = await OverrideAsync<T>(db, id, ct);
        own.Removed = removed;
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

    // ── The tuning lab: side-by-side comparisons ──

    /// <param name="recent">The pairs the page just showed, "a|b,c|d" (newest last), so the next one is different.</param>
    public async Task<TuningLabView> LabAsync(string mode, CancellationToken ct, string? recent = null)
    {
        if (!TuningLab.IsMode(mode)) throw new TrackerValidationException("Mode must be Stim, Ender or Offer.");
        await using var db = runtimes.Current.Factory.Open();
        return await LabViewAsync(db, mode, ct, recent);
    }

    /// <summary>Records which of two assignments he would rather be given (the winner is the easier one).</summary>
    public async Task<TuningLabView> CompareAsync(ComparisonRequest request, CancellationToken ct)
    {
        if (request is null || !TuningLab.IsMode(request.Mode)) throw new TrackerValidationException("Mode must be Stim or Ender.");
        if (request.Strength is < 1 or > 3) throw new TrackerValidationException("Strength runs from 1 to 3.");
        if (request.WinnerKey == request.LoserKey) throw new TrackerValidationException("Compare two different things.");
        await using var db = runtimes.Current.Factory.Open();
        var known = TuningLab.Keys(request.Mode, await db.MethodsAsync(ct), await db.EndersAsync(ct), await db.OfferDefinitionsAsync(ct));
        if (!known.Contains(request.WinnerKey) || !known.Contains(request.LoserKey)) throw new TrackerValidationException("Unknown option.");
        db.Db.Add(new TuningComparisonEntry
        {
            UserId = db.UserId, Mode = request.Mode, WinnerKey = request.WinnerKey, LoserKey = request.LoserKey,
            Strength = request.Strength, TimestampUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return await LabViewAsync(db, request.Mode, ct, request.Recent);
    }

    /// <summary>Takes back his most recent comparison in a mode.</summary>
    public async Task<TuningLabView> UndoAsync(string mode, CancellationToken ct)
    {
        if (!TuningLab.IsMode(mode)) throw new TrackerValidationException("Mode must be Stim or Ender.");
        await using var db = runtimes.Current.Factory.Open();
        var last = await db.TuningComparisons.Where(x => x.UserId == db.UserId && x.Mode == mode).OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct)
            ?? throw new TrackerValidationException("Nothing to undo.");
        db.Db.Remove(last);
        await db.SaveChangesAsync(ct);
        return await LabViewAsync(db, mode, ct);
    }

    /// <summary>What Apply would set for each method, from every stim comparison so far.</summary>
    public async Task<IReadOnlyList<LabMethodRow>> SuggestionsAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        return TuningLab.Suggestions(await db.MethodsAsync(ct), await db.TuningComparisons.AsNoTracking().ToListAsync(ct));
    }

    /// <summary>Reprices every deal he has compared by the factor his answers suggest, as personal overrides.</summary>
    public async Task<object> OptimizeDealsAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        var view = DealLab.View(await db.OfferDefinitionsAsync(ct), await db.MethodsAsync(ct), await db.TuningComparisons.AsNoTracking().ToListAsync(ct),
            db.UserId, 0, new Random(1));
        var changed = view.Deals.Where(x => x.Comparisons > 0 && Math.Abs(x.Factor - 1) > 0.01).ToList();
        if (changed.Count == 0) throw new TrackerValidationException("Compare some deals first.");
        foreach (var row in changed)
        {
            var own = await OverrideAsync<OfferDefinition>(db, row.Id, ct);
            own.BasePoints = Math.Round(own.BasePoints * row.Factor, 2);
            own.PointsPerUnit = Math.Round(own.PointsPerUnit * row.Factor, 2);
            own.PointsPerHoldSecond = Math.Round(own.PointsPerHoldSecond * row.Factor, 3);
        }
        await db.SaveChangesAsync(ct);
        return new { Deals = changed };
    }

    private static async Task<TuningLabView> LabViewAsync(GuidedData db, string mode, CancellationToken ct, string? shown = null)
    {
        var history = await db.TuningComparisons.AsNoTracking().ToListAsync(ct);
        // His last few answers in this mode, and what the page just showed, are not asked again straight away.
        var recent = LabRecent.From(shown, history.Where(x => x.Mode == mode && x.UserId == db.UserId).OrderBy(x => x.Id).TakeLast(6));
        return mode == DealLab.Mode
            ? DealLab.View(await db.OfferDefinitionsAsync(ct), await db.MethodsAsync(ct), history, db.UserId, await RecallStore.PaceAsync(db, ct), Random.Shared, recent)
            : TuningLab.View(mode, await db.MethodsAsync(ct), await db.EndersAsync(ct), history, db.UserId, await RecallStore.PaceAsync(db, ct), Random.Shared, recent);
    }

    public async Task<object> OptimizeAsync(CancellationToken ct)
    {
        await using var db = runtimes.Current.Factory.Open();
        var methods = await db.MethodsAsync(ct);
        StimMultiplierOptimizer.OptimizationResult result;
        try { result = StimMultiplierOptimizer.Optimize(methods, await db.TuningComparisons.AsNoTracking().ToListAsync(ct)); }
        catch (InvalidOperationException ex) { throw new TrackerValidationException(ex.Message); }
        var before = methods.ToDictionary(x => x.Id, x => StimMethodPointMath.GetSeedMultipliers(x));
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
            // What each method earned before, so the lab can show the change.
            Before = before.ToDictionary(x => x.Key, x => new { TimePointsPerSecond = x.Value.TimePointsPerSecond, EdgePointsPerEdge = x.Value.EdgePointsPerEdge }),
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
        if (value is OfferDefinition o && (string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(o.Wording) || !Enum.IsDefined(o.Kind)
            || !Enum.IsDefined(o.Flavor) || o.MinAmount < 1 || o.MaxAmount < o.MinAmount || o.MaxAmount > 1000 || o.MinHold < 0 || o.MaxHold < o.MinHold
            || o.MaxHold > 600 || !double.IsFinite(o.BasePoints) || o.BasePoints < 0 || !double.IsFinite(o.PointsPerUnit) || o.PointsPerUnit < 0
            || !double.IsFinite(o.PointsPerHoldSecond) || o.PointsPerHoldSecond < 0 || o.Moods is null
            || o.Moods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => !Enum.TryParse<AIDome.Sessions.DommeMood>(x, true, out _))))
            throw new TrackerValidationException("Offer values must name a price, use ranges that make sense, nonnegative points and known moods.");
    }
}
