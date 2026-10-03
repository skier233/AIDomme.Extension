namespace AIDome.Sessions;

/// <summary>
/// Points are the session clock: when they reach the budget the domme decides the ending. The
/// rules are identical in every mode, so the same activity is worth the same whoever sets the
/// pace. Only context varies the value a little: the method (when known), stroke speed, and how
/// deep into the session an edge comes.
/// </summary>
public static class Points
{
    /// <summary>
    /// Points a typical user earns per minute of play (strokes, edges and holds, with rests between),
    /// as the shipped methods score it: about 4. Each user's own pace replaces it as his sessions
    /// are recorded.
    /// </summary>
    public const double DefaultPointsPerMinute = 4.0;

    /// <summary>Stimulation that goes unreported longer than this is not counted (the user may have stepped away).</summary>
    public static readonly TimeSpan MaxUnreportedStimulation = TimeSpan.FromMinutes(5);

    /// <summary>The session's points budget: the minutes his Length means, at his pace.</summary>
    public static double Budget(int length, bool punishment, SessionLengths? lengths = null, Recall? recall = null)
        => (lengths ?? new()).Minutes(length) * Pace(recall) * (punishment ? 1.25 : 1.0);

    /// <summary>
    /// His points per minute: the default, pulled toward what his recent sessions show as they
    /// accumulate (three sessions count as much as the default), and never more than a factor of
    /// two away from it.
    /// </summary>
    public static double Pace(Recall? recall)
    {
        if (recall is not { PointsPerMinute: double seen, PaceSessions: > 0 and var n }) return DefaultPointsPerMinute;
        double observed = Math.Clamp(seen, DefaultPointsPerMinute / 2, DefaultPointsPerMinute * 2);
        return (DefaultPointsPerMinute * 3 + observed * n) / (3 + n);
    }

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

    /// <summary>
    /// What his state adds to a second of stimulation: time spent close is worth more than the same
    /// seconds far from the edge, and more again on an intense method while he is close. It depends
    /// only on her estimate of his closeness and the method, so it is the same in every mode. It is
    /// about 1 for a middling method at a typical closeness, so a session's pace is what it was.
    /// </summary>
    public static double StateFactor(double closeness, MethodInfo method)
        => 0.6 + 0.45 * Math.Clamp(closeness, 0, 1) + (closeness >= 0.75 ? 0.12 * method.Intensity / 10.0 : 0);

    /// <summary>A method counts as torturous for the session's profile from here up.</summary>
    public const int Torturous = 5;

    /// <summary>Holding at the edge.</summary>
    public static double ForHold(int seconds) => seconds <= 0 ? 0 : seconds / 5.0 + 1;
}
