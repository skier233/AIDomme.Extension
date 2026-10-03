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
    /// <summary>The tolerance he set (or the default), before anything she has learned moved it.</summary>
    public double ToleranceBase { get; set; } = 45;
    /// <summary>What he chose, in words: "a while", or "set in the lab".</summary>
    public string ToleranceSetting { get; set; } = "default";
    /// <summary>Set in the lab: learning leaves them alone.</summary>
    public bool PaceFixed { get; set; }
    public bool RecoveryFixed { get; set; }

    /// <summary>What he has taken moves his tolerance slowly, and only so far from what he set.</summary>
    public const double ToleranceUp = 2.0, ToleranceDown = 0.6;

    public string PaceSource => PaceFixed ? "set in the lab" : PaceEvidence >= 1 ? $"learned from {PaceEvidence:0} edges" : "default";
    public string RecoverySource => RecoveryFixed ? "set in the lab" : RecoveryEvidence >= 1 ? $"learned from {RecoveryEvidence:0} edges" : "default";
    public string ToleranceSource => Math.Abs(Tolerance - ToleranceBase) < 0.5 ? ToleranceSetting
        : $"{ToleranceSetting} ({ToleranceBase:0}s), moved {(Tolerance > ToleranceBase ? "up" : "down")} by what he has taken";

    /// <summary>His profile as a session starts: what he set, and what she has learned so far.</summary>
    public static ProfileState From(ProfileSettings settings, LearnedProfile learned)
    {
        double set = settings.ToleranceSeconds ?? ToleranceFor(settings.Tolerance);
        return new()
        {
            Pace = settings.Pace ?? learned.Pace, PaceFixed = settings.Pace is not null,
            // What earlier sessions taught her counts, but tonight can still move it.
            PaceEvidence = settings.Pace is not null ? 0 : Math.Min(6, learned.PaceEvidence),
            Recovery = settings.Recovery ?? learned.Recovery, RecoveryFixed = settings.Recovery is not null,
            RecoveryEvidence = settings.Recovery is not null ? 0 : Math.Min(6, learned.RecoveryEvidence),
            ToleranceBase = set,
            Tolerance = set * Math.Clamp(learned.ToleranceFactor, ToleranceDown, ToleranceUp),
            ToleranceSetting = settings.ToleranceSeconds is not null ? "set in the lab" : settings.Tolerance switch
            {
                ToleranceLevel.Briefly => "his setting: briefly", ToleranceLevel.ALongTime => "his setting: a long time", _ => "his setting: a while",
            },
        };
    }

    /// <summary>He was pushed close to his limit by her reckoning and did not go over: he can take a little more than she thought.</summary>
    public void Survived() => Tolerance = Math.Min(ToleranceBase * ToleranceUp, Tolerance * 1.03);

    /// <summary>He went over when she thought he had plenty left. One slip says little, so it moves little.</summary>
    public void Slipped() => Tolerance = Math.Max(ToleranceBase * ToleranceDown, Tolerance * 0.93);

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
    /// <summary>His control when the run began, and his tolerance on the grip he is on: for setting her estimate right when the edge comes.</summary>
    public double RunControlStart { get; set; } = 1;
    public double RunTolerance { get; set; } = 45;

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
    /// <summary>Her estimate of his control below which getting through it counts as evidence about his tolerance.</summary>
    public const double SurvivedBelow = 0.3;

    /// <summary>How much of his arousal leaks away while he is being stimulated, relative to resting.</summary>
    public const double LeakWhileStroking = 0.25;
    /// <summary>The longest she will reckon an edge can take; beyond it the method cannot get him there.</summary>
    public const double Unreachable = 900;

    /// <summary>How hard a method drives him toward the edge at a typical pace: the seconds it would take from cold if nothing leaked away.</summary>
    public static double BaseSeconds(MethodInfo? m)
    {
        double reach = Math.Max(0.5, m?.Closeness ?? 5);
        return 45 * Math.Pow(10 / reach, 0.8);
    }

    public static double SpeedFactor(int speed) => speed is >= 1 and <= 10 ? 0.25 + speed * 0.125 : 1.0;

    /// <summary>The drive of this method at this speed for him, as seconds from cold with no leak (see <see cref="ExpectedSeconds"/> for the real time).</summary>
    public static double TimeToEdge(ProfileState p, MethodInfo? m, int speed, double need)
        => Math.Max(8, p.Pace * Typical(m, speed, need));

    private static double Typical(MethodInfo? m, int speed, double need) => BaseSeconds(m) / SpeedFactor(speed) * (1 - 0.3 * need);

    /// <summary>The rate his closeness leaks away while stroking: a weak method at a crawl plateaus well short of the edge.</summary>
    public static double Leak(ProfileState p, double need) => Math.Log(2) / RecoveryHalfLife(p, need) * LeakWhileStroking;

    /// <summary>Where a method at a speed levels off for him (above 1: it reaches the edge).</summary>
    public static double Plateau(ProfileState p, MethodInfo? m, int speed, double need) => 1 / (TimeToEdge(p, m, speed, need) * Leak(p, need));

    /// <summary>Seconds for him to reach the edge from <paramref name="from"/> on this method at this speed; <see cref="Unreachable"/> when it cannot get him there.</summary>
    public static double ExpectedSeconds(ProfileState p, MethodInfo? m, int speed, double need, double from)
        => ToEdge(TimeToEdge(p, m, speed, need), Leak(p, need), from);

    private static double ToEdge(double drive, double leak, double from)
    {
        double plateau = 1 / (drive * leak);
        if (plateau <= 1.02) return Unreachable;
        return Math.Min(Unreachable, -Math.Log((plateau - 1) / (plateau - Math.Min(from, 0.99))) / leak);
    }

    /// <summary>Seconds of rest, from where he is, for his control to come back to <paramref name="to"/>.</summary>
    public static double RestToRegain(ProfileState p, double need, double closeness, double control, double to)
    {
        double halfLife = RecoveryHalfLife(p, need);
        int seconds = 0;
        for (; seconds < 120 && control < to; seconds++)
        {
            closeness *= Math.Pow(0.5, 1 / halfLife);
            control += Regain(closeness);
        }
        return seconds;
    }

    /// <summary>How long he can stay near the edge on a method: far longer on a mild one than an intense one.</summary>
    public static double Tolerance(ProfileState p, MethodInfo? m)
        => Math.Max(5, p.Tolerance * Math.Pow(2, (5 - (m?.Intensity ?? 5)) / 3.5));

    public static double RecoveryHalfLife(ProfileState p, double need) => Math.Max(5, p.Recovery * (1 + 0.6 * need));

    /// <summary>Seconds of rest for his closeness to fall from <paramref name="from"/> to <paramref name="to"/>.</summary>
    public static double RestFor(ProfileState p, double need, double from, double to)
        => to >= from || to <= 0 ? 0 : RecoveryHalfLife(p, need) * Math.Log2(from / to);

    /// <summary>
    /// How fast his control comes back, per second at rest: quickly when he is well down, hardly at
    /// all while he is kept high. This is what makes the gap after an edge her lever.
    /// </summary>
    public static double Regain(double closeness) => Math.Max(0, AfterEdge - closeness) / AfterEdge / 30;

    /// <summary>How fast a method builds overwhelm, per second: only torture does.</summary>
    public static double OverwhelmRate(MethodInfo? m, int speed)
    {
        double torture = (m?.TortLevel ?? 0) / 10.0;
        return torture * torture * (0.5 + 0.07 * (speed is >= 1 and <= 10 ? speed : 5)) / 110;
    }

    /// <summary>
    /// Moves the state forward by <paramref name="seconds"/> in one mode. <paramref name="cap"/> limits
    /// closeness while stroking (her estimate; 1 for the truth).
    /// </summary>
    public static void Advance(BodyState b, ProfileState p, BodyMode mode, MethodInfo? method, int speed, double seconds, double cap = 1)
    {
        if (seconds <= 0) return;
        seconds = Math.Min(seconds, 3600);
        // A hold that ended with his control nearly spent, and no slip: evidence he can take it.
        if (cap < 1 && mode != BodyMode.Hold && b.LastMode == BodyMode.Hold && b.Control < SurvivedBelow) p.Survived();
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
            b.RunControlStart = b.Control;
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
                    double typical = Typical(method, speed, b.Need);
                    // The method drives him up while some of it leaks away, so a weak one levels off.
                    // Above the cap (he is keeping himself under the edge) he eases back to it.
                    double next = b.Closeness + dt / t - dt * Leak(p, b.Need) * b.Closeness;
                    if (next > cap) next = b.Closeness <= cap ? cap : Math.Max(cap, b.Closeness * Math.Pow(0.5, dt / RecoveryHalfLife(p, b.Need)));
                    b.Closeness = Math.Clamp(next, 0, 1);
                    b.RunWork += dt / Math.Max(8, typical);
                    b.RunSeconds += dt;
                    b.Overwhelm = Math.Min(1, b.Overwhelm + dt * OverwhelmRate(method, speed));
                    if ((method?.TortLevel ?? 0) < 3) b.Overwhelm = Math.Max(0, b.Overwhelm - dt / 240);
                    // The last stretch before the edge costs him more the nearer he is; a hold costs it in full.
                    // While her estimate waits at its cap for him to report the edge she does not know where
                    // he is, so nothing is charged; the edge itself settles the account (see OnEdge).
                    bool waiting = cap < 1 && b.Closeness >= cap - 1e-6;
                    b.RunTolerance = Tolerance(p, method);
                    if (b.Closeness >= NearEdge) { if (!waiting) b.Control = Math.Max(0, b.Control - dt * (b.Closeness - NearEdge) / (1 - NearEdge) / b.RunTolerance); }
                    else b.Control = Math.Min(1, b.Control + dt * Regain(b.Closeness) / 2);
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
                    b.Control = Math.Min(1, b.Control + dt * Regain(b.Closeness));
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
    public static void OnEdge(BodyState b, ProfileState p, bool learn, double atSeconds, bool estimate = false)
    {
        if (estimate && b.RunSeconds >= 1 && b.RunWork > 0 && b.RunCap >= EstimateCap)
        {
            // Now she knows when he got there, so she knows roughly how long he spent on the last
            // stretch, and what that cost him: her running guess is replaced by that.
            double leak = Leak(p, b.Need), decay = Math.Exp(-leak * b.RunSeconds), from = Math.Min(b.RunStartCloseness, 0.97);
            double plateau = Math.Max(1.02, (1 - from * decay) / (1 - decay));
            double near = from >= NearEdge ? b.RunSeconds : Math.Min(b.RunSeconds, -Math.Log((plateau - 1) / (plateau - NearEdge)) / leak);
            b.Control = Math.Clamp(b.RunControlStart - near * 0.5 / b.RunTolerance, 0, 1);
            if (b.Control < SurvivedBelow) p.Survived();
        }
        if (learn && b.RunSeconds >= 3 && b.RunWork > 0 && b.RunCap >= EstimateCap)
        {
            // The run as one stretch at its average drive: what she expected, and what his pace or
            // his recovery must have been for it to take as long as it did.
            double drive = b.RunSeconds / b.RunWork, leak = Leak(p, b.Need), decay = Math.Exp(-leak * b.RunSeconds);
            double from = Math.Min(b.RunStartCloseness, 0.97);
            double expected = ToEdge(p.Pace * drive, leak, from);
            b.Checks.Add(new(atSeconds, b.RunMethod, b.RunStartCloseness, expected, b.RunSeconds, b.RunRestBefore));
            if (b.Checks.Count > 300) b.Checks.RemoveAt(0);

            if (b.RunRestBefore < 2)
            {
                // No rest to account for: the run measures his pace. From low down it is a clean
                // measure; straight after an edge it rests on a guess at how far he eased off.
                double weight = b.RunRestBefore < 0 && b.RunStartCloseness <= 0.6 ? 1 : 0.3;
                double plateau = (1 - from * decay) / (1 - decay);
                double sample = Math.Clamp(1 / (leak * plateau * drive), 0.3, 3);
                p.Pace = Blend(p.Pace, p.PaceEvidence, sample, weight);
                p.PaceEvidence = Math.Min(30, p.PaceEvidence + weight);
            }
            else
            {
                // After a rest following an edge: how far he had come down measures his recovery.
                double plateau = 1 / (p.Pace * drive * leak);
                double start = Math.Clamp(plateau + (1 - plateau) / decay, 0.03, 0.97);
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

    /// <summary>He came without permission. If she thought he had plenty of control left, her idea of his tolerance was too generous.</summary>
    public static void OnSlip(BodyState b, ProfileState p)
    {
        if (b.Control > 0.3) p.Slipped();
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
        b.CurrentAbove80 = 0;
    }

    /// <summary>A new sample moves a belief in proportion to how little evidence it rests on.</summary>
    private static double Blend(double current, double evidence, double sample, double weight)
    {
        const double prior = 1;
        return (current * (prior + evidence) + sample * weight) / (prior + evidence + weight);
    }
}
