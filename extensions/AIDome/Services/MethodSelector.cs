using AIDome2025.Data;

namespace AIDome2025.Services;

/// <summary>
/// Shared method selection logic used by both online and offline engines.
/// Handles weighted random selection with anti-repetition, lube awareness,
/// and attribute-biased picks.
/// </summary>
public static class MethodSelector
{
    /// <summary>
    /// Pick a method from the pool with weighted randomness.
    /// </summary>
    public static StimMethod Pick(
        List<StimMethod> eligible,
        List<int> recentMethodIds,
        int intensityCeiling,
        bool lubeActive,
        bool preferTease = false,
        bool preferTorture = false,
        bool preferCloseness = false)
    {
        var pool = eligible.Where(m => m.Intensity <= intensityCeiling).ToList();
        if (pool.Count == 0) pool = eligible.ToList();
        if (pool.Count == 0) return Fallback();

        int lastId = recentMethodIds.Count > 0 ? recentMethodIds[^1] : -1;

        var weighted = pool.Select(m =>
        {
            int idx = recentMethodIds.LastIndexOf(m.Id);
            double w = idx >= 0 ? 1.0 / (1 + (recentMethodIds.Count - idx)) : 2.0;
            // Near-zero weight for the method just used
            if (m.Id == lastId && pool.Count > 1) w *= 0.02;
            if (preferTease) w *= 1.0 + m.Tease / 10.0;
            if (preferTorture) w *= 1.0 + m.TortLevel / 10.0;
            if (preferCloseness) w *= 1.0 + m.Closeness / 5.0;
            // Lube state: once active, strongly suppress dry methods; if not active, mildly suppress lube
            if (lubeActive && !m.RequiresLube) w *= 0.01;
            else if (!lubeActive && m.RequiresLube) w *= 0.4;
            return (m, w);
        }).ToList();

        return WeightedSelect(weighted) ?? pool[Random.Shared.Next(pool.Count)];
    }

    /// <summary>
    /// Pick a punishment ender appropriate for the given settings.
    /// Uses near-uniform weight so all eligible enders appear with good variety.
    /// </summary>
    public static PunishmentEnder PickEnder(
        List<PunishmentEnder> enders,
        int severityFloor,
        bool allowBondage,
        bool allowCEI,
        bool allowSelfOral)
    {
        var pool = enders.Where(e =>
        {
            if (e.RequiresBondage && !allowBondage) return false;
            if (e.RequiresCEI && !allowCEI) return false;
            if (e.RequiresSelfOral && !allowSelfOral) return false;
            return e.Severity >= severityFloor;
        }).ToList();

        if (pool.Count == 0)
            pool = enders.Where(e => !e.RequiresBondage && !e.RequiresCEI && !e.RequiresSelfOral).ToList();
        if (pool.Count == 0)
            pool = enders.ToList();

        // Near-uniform + small severity tilt so variety is high
        var weighted = pool.Select(e =>
        {
            double w = 5.0 + e.Severity * 0.3 + Random.Shared.NextDouble() * 2.0;
            return (e, w);
        }).ToList();

        return WeightedSelect(weighted) ?? pool[Random.Shared.Next(pool.Count)];
    }

    /// <summary>Track a method in the recent history list.</summary>
    public static void Track(List<int> recentIds, StimMethod method, ref bool lubeActive)
    {
        recentIds.Add(method.Id);
        if (recentIds.Count > 10)
            recentIds.RemoveAt(0);
        if (method.RequiresLube) lubeActive = true;
    }

    /// <summary>
    /// Torturous and frustrating tease methods use timed duration.
    /// Everything else uses edge counts.
    /// </summary>
    public static bool ShouldUseTimed(StimMethod m)
    {
        if (m.TortLevel >= 5 && m.Closeness <= 3) return true;
        if (m.Tease >= 6 && m.Closeness <= 3) return true;
        return false;
    }

    /// <summary>Filter methods based on session toy/kink flags.</summary>
    public static List<StimMethod> FilterForSession(List<StimMethod> all, SessionRecord s)
    {
        return all.Where(m =>
        {
            if (m.ToyCategory == ToyCategory.None) return true;
            if (!s.AllowToys) return false;
            return m.ToyCategory switch
            {
                ToyCategory.HeadVibrator => s.AllowVibrator,
                ToyCategory.Fleshlight => s.AllowFleshlight,
                ToyCategory.Autoblow => s.AllowAutoblow,
                ToyCategory.ProstateMassager => s.AllowProstate,
                _ => false
            };
        }).ToList();
    }

    /// <summary>Add jitter to a value (±20%).</summary>
    public static int Jitter(int value)
    {
        double factor = 1.0 + (Random.Shared.NextDouble() - 0.5) * 0.4;
        return Math.Max(10, (int)(value * factor));
    }

    public static StimMethod Fallback() => new()
    {
        Id = 0,
        Name = "Standard stroke",
        Description = "Use your dominant hand at a comfortable pace."
    };

    private static T? WeightedSelect<T>(List<(T item, double w)> weighted) where T : class
    {
        double total = weighted.Sum(x => x.w);
        if (total <= 0) return null;
        double roll = Random.Shared.NextDouble() * total;
        double cum = 0;
        foreach (var (item, w) in weighted)
        {
            cum += w;
            if (roll <= cum) return item;
        }
        return null;
    }

    public static T WeightedSelect<T>(List<T> items, Func<T, double> weightFunc) where T : class
    {
        var weighted = items.Select(i => (item: i, w: weightFunc(i))).ToList();
        return WeightedSelect(weighted) ?? items[Random.Shared.Next(items.Count)];
    }
}
