using System;

namespace AIDome2025.Utils;

public static class RandomHelper
{
    /// <summary>
    /// Returns true approximately <paramref name="percent" /> percent of the time.
    /// Percent should be in range 0..100. Values outside will be clamped.
    /// Uses <see cref="Random.Shared"/> under the covers.
    /// </summary>
    public static bool PercentChance(double percent)
    {
        if (percent <= 0) return false;
        if (percent >= 100) return true;

        var r = Random.Shared.NextDouble(); // 0.0 .. 1.0
        return r < (percent / 100.0);
    }
}
