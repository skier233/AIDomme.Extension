namespace AIDome.Sessions;

/// <summary>
/// Points are the session clock: when they reach the budget the domme decides the ending. The
/// rules are identical in every mode, so the same activity is worth the same whoever sets the
/// pace. Only context varies the value a little: the method (when known), stroke speed, and how
/// deep into the session an edge comes.
/// </summary>
public static class Points
{
    /// <summary>Budget by Length 1–10. Calibrated for per-edge plus per-second scoring.</summary>
    private static readonly int[] LengthBudget = [20, 33, 52, 72, 98, 130, 170, 215, 275, 350];

    /// <summary>Stimulation that goes unreported longer than this is not counted (the user may have stepped away).</summary>
    public static readonly TimeSpan MaxUnreportedStimulation = TimeSpan.FromMinutes(5);

    public static double Budget(int length, bool punishment)
        => LengthBudget[Math.Clamp(length, 1, 10) - 1] * (punishment ? 1.25 : 1.0);

    /// <summary>One recorded edge. Later edges in a session are worth slightly more (up to +30%).</summary>
    public static double ForEdge(MethodInfo method, int edgesBefore)
        => method.EdgePoints * (1.0 + Math.Min(0.3, edgesBefore * 0.02));

    /// <summary>Seconds of stimulation. Speed 0 means the user sets the pace and counts as average.</summary>
    public static double ForStimulation(MethodInfo method, double seconds, int speed)
    {
        if (seconds <= 0) return 0;
        double speedFactor = speed is >= 1 and <= 10 ? 0.7 + 0.06 * speed : 1.0;
        return method.TimePoints * Math.Min(seconds, MaxUnreportedStimulation.TotalSeconds) * speedFactor;
    }

    /// <summary>Holding at the edge.</summary>
    public static double ForHold(int seconds) => seconds <= 0 ? 0 : seconds / 5.0 + 1;
}
