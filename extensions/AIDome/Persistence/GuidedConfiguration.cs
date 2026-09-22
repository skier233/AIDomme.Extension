using System.ComponentModel.DataAnnotations;
using AIDome.Tracking;
using AIDome.Data;
using AIDome.Services;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record DefinitionView<T>(T Value, bool IsOverride, T? SharedDefault);

public sealed class GuidedConfiguration(GuidedRuntimes runtimes)
{
    public async Task<object> SnapshotAsync(CancellationToken ct)
    {
        await using var db = await runtimes.Current.Factory.CreateDbContextAsync(ct);
        return new
        {
            Methods = await ViewsAsync<StimMethod>(db, ct),
            Enders = await ViewsAsync<PunishmentEnder>(db, ct),
            Ratings = await ViewsAsync<TuningRatingEntry>(db, ct),
            Comparisons = await ViewsAsync<TuningComparisonEntry>(db, ct),
        };
    }

    private static async Task<List<DefinitionView<T>>> ViewsAsync<T>(CommandCenterDbContext db, CancellationToken ct) where T : class
    {
        var shared = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(x => EF.Property<int>(x, "OwnerId") == 0).ToListAsync(ct);
        var own = await db.Set<T>().ToListAsync(ct);
        return own.OrderBy(x => (int)typeof(T).GetProperty("Id")!.GetValue(x)!).Select(x => new DefinitionView<T>(x,
            (bool)db.Entry(x).Property("IsOverride").CurrentValue!,
            shared.FirstOrDefault(s => Equals(typeof(T).GetProperty("Id")!.GetValue(s), typeof(T).GetProperty("Id")!.GetValue(x))))).ToList();
    }

    public async Task<T> SaveAsync<T>(int id, T value, CancellationToken ct) where T : class
    {
        Validate(value);
        if (id != (int)typeof(T).GetProperty("Id")!.GetValue(value)!) throw new TrackerValidationException("Definition ID cannot change.");
        await using var db = await runtimes.Current.Factory.CreateDbContextAsync(ct);
        var own = await db.Set<T>().SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == id, ct)
            ?? throw new TrackerValidationException("Definition not found.");
        GuidedStore.CopyScalars(db, own, value);
        // Saving even an identical value is an explicit personal override.
        db.Entry(own).Property("IsOverride").CurrentValue = true;
        await db.SaveChangesAsync(ct);
        return own;
    }

    public async Task<T> ResetAsync<T>(int id, CancellationToken ct) where T : class
    {
        await using var db = await runtimes.Current.Factory.CreateDbContextAsync(ct);
        var shared = await db.Set<T>().IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => EF.Property<int>(x, "OwnerId") == 0 && EF.Property<int>(x, "Id") == id, ct)
            ?? throw new TrackerValidationException("No shared default exists for this definition.");
        var own = await db.Set<T>().SingleOrDefaultAsync(x => EF.Property<int>(x, "Id") == id, ct)
            ?? throw new TrackerValidationException("Definition not found.");
        db.ApplyingDefaults = true;
        GuidedStore.CopyScalars(db, own, shared);
        db.Entry(own).Property("IsOverride").CurrentValue = false;
        await db.SaveChangesAsync(ct);
        return own;
    }

    public async Task<object> OptimizeAsync(CancellationToken ct)
    {
        await using var db = await runtimes.Current.Factory.CreateDbContextAsync(ct);
        var methods = await db.StimMethods.ToListAsync(ct);
        StimMultiplierOptimizer.OptimizationResult result;
        try { result = StimMultiplierOptimizer.Optimize(methods, await db.TuningComparisonHistory.ToListAsync(ct)); }
        catch (InvalidOperationException ex) { throw new TrackerValidationException(ex.Message); }
        foreach (var method in methods)
        {
            var multiplier = result.Multipliers[method.Id];
            method.TimePointMultiplier = multiplier.TimePointsPerSecond;
            method.EdgePointMultiplier = multiplier.EdgePointsPerEdge;
        }
        await db.SaveChangesAsync(ct);
        return new
        {
            result.WinningAlgorithm, result.ValidationLogLoss, result.ValidationAccuracy, result.Trials,
            Multipliers = result.Multipliers.ToDictionary(x => x.Key, x => new { TimePointsPerSecond = x.Value.TimePointsPerSecond, EdgePointsPerEdge = x.Value.EdgePointsPerEdge }),
        };
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
