using AIDomme.Data;
using AIDomme.Services;

namespace AIDomme.Sessions;

/// <summary>Randomness the session engine uses. Injected so sessions are reproducible in tests.</summary>
public interface IRandom
{
    /// <summary>Uniform integer in [min, max).</summary>
    int Next(int min, int max);
    /// <summary>Uniform double in [0, 1).</summary>
    double NextDouble();
}

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemRandom : IRandom
{
    public static readonly SystemRandom Instance = new();
    public int Next(int min, int max) => Random.Shared.Next(min, max);
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();
    public DateTime UtcNow => DateTime.UtcNow;
}

public static class RandomExtensions
{
    public static int Next(this IRandom random, int max) => random.Next(0, max);
    public static bool Chance(this IRandom random, double percent) => random.NextDouble() * 100 < percent;
    public static T Pick<T>(this IRandom random, IReadOnlyList<T> items) => items[random.Next(0, items.Count)];

    /// <summary>Weighted pick; entries with non-positive weight are never chosen.</summary>
    public static T Weighted<T>(this IRandom random, IReadOnlyList<(T Item, double Weight)> pool, T fallback)
    {
        double total = pool.Where(x => x.Weight > 0).Sum(x => x.Weight);
        if (total <= 0) return fallback;
        double roll = random.NextDouble() * total, cumulative = 0;
        foreach (var (item, weight) in pool)
        {
            if (weight <= 0) continue;
            cumulative += weight;
            if (roll < cumulative) return item;
        }
        return fallback;
    }

    /// <summary>±20% variation so durations never feel mechanical.</summary>
    public static int Jitter(this IRandom random, int value)
        => Math.Max(10, (int)(value * (1.0 + (random.NextDouble() - 0.5) * 0.4)));
}

/// <summary>
/// A stimulation method as the session sees it: a snapshot of the account's effective definition,
/// kept in session state so a running session is unaffected by later library edits.
/// </summary>
public sealed record MethodInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Intensity { get; init; }
    public int Tease { get; init; }
    public int TortLevel { get; init; }
    public int Closeness { get; init; }
    public bool RequiresLube { get; init; }
    public ToyCategory ToyCategory { get; init; }
    /// <summary>Points per second of stimulation (calibrated, or estimated from the method's scales).</summary>
    public double TimePoints { get; init; }
    /// <summary>Points per edge (calibrated, or estimated from the method's scales).</summary>
    public double EdgePoints { get; init; }
    /// <summary>True for the placeholder used when the user chooses their own method.</summary>
    public bool IsUserChoice { get; init; }

    public static MethodInfo From(StimMethod m)
    {
        var (time, edge) = StimMethodPointMath.GetSeedMultipliers(m);
        return new()
        {
            Id = m.Id, Name = m.Name, Description = m.Description, Intensity = m.Intensity, Tease = m.Tease,
            TortLevel = m.TortLevel, Closeness = m.Closeness, RequiresLube = m.RequiresLube, ToyCategory = m.ToyCategory,
            TimePoints = time, EdgePoints = edge,
        };
    }

    /// <summary>
    /// Stands in for "whatever the user is doing" when the user owns methods. Its point rates are the
    /// average of the user's eligible methods, so the same activity earns the same points in every mode.
    /// </summary>
    public static MethodInfo UserChoice(IReadOnlyList<MethodInfo> pool) => new()
    {
        Id = 0, Name = "Your choice", Description = "Any grip you like", IsUserChoice = true,
        Intensity = 5, Closeness = 5,
        TimePoints = pool.Count > 0 ? pool.Average(x => x.TimePoints) : 0.09,
        EdgePoints = pool.Count > 0 ? pool.Average(x => x.EdgePoints) : 3.0,
    };
}

/// <summary>Stroke speed 1–10 as a metronome tempo. 0 means no prescribed tempo.</summary>
public static class Tempo
{
    private static readonly int[] Bpm = [40, 55, 75, 95, 120, 145, 170, 195, 220, 250];
    public static int FromSpeed(int speed) => speed is >= 1 and <= 10 ? Bpm[speed - 1] : 0;

    /// <summary>
    /// The beat for a speed on a method: an intense grip takes fewer strokes a minute to be "fast"
    /// than a mild one, so the same word feels the same whatever she has him doing.
    /// </summary>
    public static int For(int speed, MethodInfo? method)
    {
        int bpm = FromSpeed(speed);
        if (bpm == 0 || method is null) return bpm;
        double factor = Math.Clamp(1.2 - 0.04 * method.Intensity, 0.8, 1.15);
        return (int)(Math.Round(bpm * factor / 5) * 5);
    }
}
