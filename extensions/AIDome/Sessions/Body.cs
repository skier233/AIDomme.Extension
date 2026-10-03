namespace AIDome.Sessions;

/// <summary>How long he says he can stay at the edge. The one thing about his body he is asked.</summary>
public enum ToleranceLevel { Briefly, AWhile, ALongTime }

/// <summary>
/// What she believes about his body: three numbers. Pace and recovery start from defaults and are
/// learned quickly from his edges; tolerance comes from his setting and moves slowly.
/// </summary>
public sealed class ProfileState
{
    public const double DefaultRecovery = 25;

    /// <summary>A factor on how long each method takes him to edge (1 is typical; lower is quicker).</summary>
    public double Pace { get; set; } = 1.0;
    /// <summary>How many edges that belief rests on.</summary>
    public double PaceEvidence { get; set; }
    /// <summary>Seconds for his closeness to halve while resting.</summary>
    public double Recovery { get; set; } = DefaultRecovery;
    public double RecoveryEvidence { get; set; }
    /// <summary>Seconds he can stay near the edge on a method of middling intensity before he is likely to go over.</summary>
    public double Tolerance { get; set; } = 45;
    /// <summary>Where the tolerance came from: "default", "setting" or "learned".</summary>
    public string ToleranceSource { get; set; } = "default";

    public string PaceSource => PaceEvidence >= 1 ? $"learned from {PaceEvidence:0} edges" : "default";
    public string RecoverySource => RecoveryEvidence >= 1 ? $"learned from {RecoveryEvidence:0} edges" : "default";

    public static double ToleranceFor(ToleranceLevel level) => level switch
    {
        ToleranceLevel.Briefly => 20, ToleranceLevel.ALongTime => 100, _ => 45,
    };

    public ProfileState Copy() => (ProfileState)MemberwiseClone();
}

/// <summary>One edge as she expected it and as it came: the check on her estimate.</summary>
public sealed record EdgeCheck(double AtSeconds, string Method, double ClosenessBefore, double ExpectedSeconds, double ActualSeconds, double RestBefore);

public enum BodyMode { Rest, Stroke, Hold }

/// <summary>
/// His state as she tracks it through a session. Closeness is how near the edge he is right now;
/// need is the ache built up (and makes him quicker to edge and slower to come down); control is his
/// margin before a slip; overwhelm is how much he wants it to stop. All run 0 to 1.
/// </summary>
public sealed class BodyState
{
    public double Closeness { get; set; }
    public double Need { get; set; }
    public double Control { get; set; } = 1;
    public double Overwhelm { get; set; }
    /// <summary>What need is computed from: it only goes up, until an orgasm.</summary>
    public double NeedLoad { get; set; }
    public DateTime? AsOfUtc { get; set; }

    // The current run toward an edge, for learning his pace and recovery.
    public bool JustEdged { get; set; }
    public BodyMode LastMode { get; set; }
    public double RunStartCloseness { get; set; }
    /// <summary>How much of a from-cold edge the run's stimulation amounts to, at a typical pace.</summary>
    public double RunWork { get; set; }
    public double RunSeconds { get; set; }
    /// <summary>Seconds of rest between the last edge and this run; negative when the run did not follow an edge.</summary>
    public double RunRestBefore { get; set; } = -1;
    public double RestSinceEdge { get; set; } = -1;
    public string RunMethod { get; set; } = "";
    /// <summary>The cap the run is under: only a run that was heading for the edge says anything about his pace.</summary>
    public double RunCap { get; set; } = 1;

    // For the session's summary.
    public double SecondsBelow50 { get; set; }
    public double Seconds50To75 { get; set; }
    public double Seconds75To90 { get; set; }
    public double SecondsAbove90 { get; set; }
    public double SecondsNearLimit { get; set; }
    public double LongestAbove80 { get; set; }
    public double CurrentAbove80 { get; set; }
    public double PeakNeed { get; set; }
    public double PeakOverwhelm { get; set; }
    public double LowestControl { get; set; } = 1;
    public double TotalSeconds { get; set; }
    public List<EdgeCheck> Checks { get; set; } = [];

    public BodyState Copy()
    {
        var copy = (BodyState)MemberwiseClone();
        copy.Checks = [.. Checks];
        return copy;
    }
}

/// <summary>
/// The model of his body: how stimulation, rest and holds move the four states, and what each edge
/// teaches her about his pace and recovery. The same rules run her estimate and, in simulation, the
/// simulated user's true state; only the profile differs.
/// </summary>
public static class Body
{
    /// <summary>Her estimate never claims the edge before he reports it.</summary>
    public const double EstimateCap = 0.97;
    /// <summary>In a timed step that is not asking for an edge, she assumes he keeps himself just under it.</summary>
    public const double TimedCap = 0.88;
    /// <summary>When he sets his own pace she assumes he stays comfortable.</summary>
    public const double SelfPacedCap = 0.8;
    /// <summary>Straight after an edge he has to ease off a little before he can reach another.</summary>
    public const double AfterEdge = 0.85;
    public const double NearEdge = 0.9;

    /// <summary>Seconds from cold to the edge on a method at a typical pace, before need shortens it.</summary>
    public static double BaseSeconds(MethodInfo? m)
    {
        double reach = Math.Max(0.5, m?.Closeness ?? 5);
        return 45 * Math.Pow(10 / reach, 0.8);
    }

    public static double SpeedFactor(int speed) => speed is >= 1 and <= 10 ? 0.5 + speed * 0.1 : 1.0;

    /// <summary>Seconds from cold to the edge for him, on this method at this speed, at his current need.</summary>
    public static double TimeToEdge(ProfileState p, MethodInfo? m, int speed, double need)
        => Math.Max(8, p.Pace * BaseSeconds(m) / SpeedFactor(speed) * (1 - 0.3 * need));

    /// <summary>How long he can stay near the edge on a method: far longer on a mild one than an intense one.</summary>
    public static double Tolerance(ProfileState p, MethodInfo? m)
        => Math.Max(5, p.Tolerance * Math.Pow(2, (5 - (m?.Intensity ?? 5)) / 3.5));

    public static double RecoveryHalfLife(ProfileState p, double need) => Math.Max(5, p.Recovery * (1 + 0.6 * need));

    /// <summary>Seconds of rest for his closeness to fall from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static double RestFor(ProfileState p, double need, double from, double to)
        => to >= from || to <= 0 ? 0 : RecoveryHalfLife(p, need) * Math.Log2(from / to);

    /// <summary>How fast a method builds overwhelm, per second: only torture does.</summary>
    public static double OverwhelmRate(MethodInfo? m, int speed)
    {
        double torture = (m?.TortLevel ?? 0) / 10.0;
        return torture * torture * (0.5 + 0.07 * (speed is >= 1 and <= 10 ? speed : 5)) / 160;
    }

    /// <summary>
    /// Moves the state forward by <paramref name="seconds"/> in one mode. <paramref name="cap"/> limits
    /// closeness while stroking (her estimate; 1 for the truth).
    /// </summary>
    public static void Advance(BodyState b, ProfileState p, BodyMode mode, MethodInfo? method, int speed, double seconds, double cap = 1)
    {
        if (seconds <= 0) return;
        seconds = Math.Min(seconds, 3600);
        if (mode == BodyMode.Stroke && (b.LastMode != BodyMode.Stroke || b.JustEdged || cap != b.RunCap))
        {
            // A new run toward the edge starts here.
            if (b.JustEdged && b.Closeness > AfterEdge) b.Closeness = AfterEdge;
            b.RunStartCloseness = b.Closeness;
            b.RunWork = 0;
            b.RunSeconds = 0;
            b.RunRestBefore = b.RestSinceEdge;
            b.RunMethod = method?.Name ?? "";
            b.RunCap = cap;
        }
        // Rest is counted from the last edge (or the hold that followed it) until stroking resumes.
        if (mode == BodyMode.Stroke) b.RestSinceEdge = -1;
        else if (mode == BodyMode.Hold && b.RestSinceEdge >= 0) b.RestSinceEdge = 0;
        b.JustEdged = false;
        b.LastMode = mode;

        double left = seconds;
        while (left > 0)
        {
            double dt = Math.Min(1, left);
            left -= dt;
            switch (mode)
            {
                case BodyMode.Stroke:
                {
                    double t = TimeToEdge(p, method, speed, b.Need);
                    double typical = BaseSeconds(method) / SpeedFactor(speed) * (1 - 0.3 * b.Need);
                    // Rises toward the cap; if it is already above the cap it stays where it is.
                    b.Closeness = Math.Min(Math.Min(1, Math.Max(cap, b.Closeness)), b.Closeness + dt / t);
                    b.RunWork += dt / Math.Max(8, typical);
                    b.RunSeconds += dt;
                    b.Overwhelm = Math.Min(1, b.Overwhelm + dt * OverwhelmRate(method, speed));
                    if ((method?.TortLevel ?? 0) < 3) b.Overwhelm = Math.Max(0, b.Overwhelm - dt / 240);
                    // The last stretch before the edge costs him more the nearer he is; a hold costs it in full.
                    if (b.Closeness >= NearEdge) b.Control = Math.Max(0, b.Control - dt * (b.Closeness - NearEdge) / (1 - NearEdge) / Tolerance(p, method));
                    else if (b.Closeness < 0.75) b.Control = Math.Min(1, b.Control + dt / 90);
                    break;
                }
                case BodyMode.Hold:
                    b.Closeness = 1;
                    b.Control = Math.Max(0, b.Control - dt / Tolerance(p, method));
                    b.Overwhelm = Math.Max(0, b.Overwhelm - dt / 240);
                    break;
                default:
                    b.Closeness *= Math.Pow(0.5, dt / RecoveryHalfLife(p, b.Need));
                    if (b.RestSinceEdge >= 0) b.RestSinceEdge += dt;
                    if (b.Closeness < AfterEdge) b.Control = Math.Min(1, b.Control + dt / 45);
                    b.Overwhelm = Math.Max(0, b.Overwhelm - dt / 120);
                    break;
            }
            b.NeedLoad += dt / 60 * b.Closeness * b.Closeness * 0.05;
            b.Need = 1 - Math.Exp(-b.NeedLoad);

            // Bookkeeping for the summary.
            b.TotalSeconds += dt;
            if (b.Closeness >= 0.9) b.SecondsAbove90 += dt;
            else if (b.Closeness >= 0.75) b.Seconds75To90 += dt;
            else if (b.Closeness >= 0.5) b.Seconds50To75 += dt;
            else b.SecondsBelow50 += dt;
            if (b.Control < 0.35) b.SecondsNearLimit += dt;
            b.CurrentAbove80 = b.Closeness >= 0.8 ? b.CurrentAbove80 + dt : 0;
            b.LongestAbove80 = Math.Max(b.LongestAbove80, b.CurrentAbove80);
            b.PeakNeed = Math.Max(b.PeakNeed, b.Need);
            b.PeakOverwhelm = Math.Max(b.PeakOverwhelm, b.Overwhelm);
            b.LowestControl = Math.Min(b.LowestControl, b.Control);
        }
    }

    /// <summary>
    /// He reported an edge: closeness is 100% by definition. When <paramref name="learn"/> is set (she
    /// chose the method and the pace, so the run is comparable), the run teaches her his pace or his
    /// recovery, and the expectation is recorded beside what happened.
    /// </summary>
    public static void OnEdge(BodyState b, ProfileState p, bool learn, double atSeconds)
    {
        if (learn && b.RunSeconds >= 3 && b.RunWork > 0 && b.RunCap >= EstimateCap)
        {
            double gap = Math.Max(0.03, 1 - b.RunStartCloseness);
            double expected = gap * p.Pace * (b.RunSeconds / b.RunWork);
            b.Checks.Add(new(atSeconds, b.RunMethod, b.RunStartCloseness, expected, b.RunSeconds, b.RunRestBefore));
            if (b.Checks.Count > 300) b.Checks.RemoveAt(0);

            if (b.RunRestBefore < 2)
            {
                // No rest to account for: the run measures his pace. From low down it is a clean
                // measure; straight after an edge it rests on a guess at how far he eased off.
                double weight = b.RunRestBefore < 0 && b.RunStartCloseness <= 0.6 ? 1 : 0.3;
                double sample = Math.Clamp(b.RunWork / gap, 0.3, 3);
                p.Pace = Blend(p.Pace, p.PaceEvidence, sample, weight);
                p.PaceEvidence = Math.Min(30, p.PaceEvidence + weight);
            }
            else
            {
                // After a rest following an edge: how far he had come down measures his recovery.
                double start = Math.Clamp(1 - b.RunWork / p.Pace, 0.03, 0.97);
                double halfLife = b.RunRestBefore * Math.Log(0.5) / Math.Log(start) / (1 + 0.6 * b.Need);
                p.Recovery = Blend(p.Recovery, p.RecoveryEvidence, Math.Clamp(halfLife, 5, 180), 1);
                p.RecoveryEvidence = Math.Min(30, p.RecoveryEvidence + 1);
            }
        }
        b.Closeness = 1;
        b.JustEdged = true;
        b.RestSinceEdge = 0;
        b.NeedLoad += 0.04;
        b.Need = 1 - Math.Exp(-b.NeedLoad);
    }

    /// <summary>An orgasm: the edge is gone, most of the need with it, and his control is his own again.</summary>
    public static void OnOrgasm(BodyState b)
    {
        b.Closeness = 0;
        b.NeedLoad *= 0.2;
        b.Need = 1 - Math.Exp(-b.NeedLoad);
        b.Control = 1;
        b.JustEdged = false;
        b.RestSinceEdge = -1;
    }

    /// <summary>A new sample moves a belief in proportion to how little evidence it rests on.</summary>
    private static double Blend(double current, double evidence, double sample, double weight)
    {
        const double prior = 1;
        return (current * (prior + evidence) + sample * weight) / (prior + evidence + weight);
    }
}
