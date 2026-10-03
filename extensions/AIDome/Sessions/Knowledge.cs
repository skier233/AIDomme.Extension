using AIDome.Data;
using AIDome.Sessions.Plan;
using AIDome.Speech;

namespace AIDome.Sessions;

/// <summary>
/// What he tells her about his body. Only tolerance is asked: how long he can stay at the edge is
/// the one thing she cannot see quickly for herself. Pace and recovery she learns from his edges;
/// the numbers here are overrides for the tuning lab, empty unless he sets them.
/// </summary>
public sealed record ProfileSettings
{
    public ToleranceLevel Tolerance { get; init; } = ToleranceLevel.AWhile;
    /// <summary>The exact tolerance in seconds, instead of the plain choice.</summary>
    public double? ToleranceSeconds { get; init; }
    public double? Pace { get; init; }
    public double? Recovery { get; init; }

    public ProfileSettings Validate()
    {
        if (!Enum.IsDefined(Tolerance)) throw new ArgumentException("Unknown tolerance.");
        if (ToleranceSeconds is < 5 or > 300) throw new ArgumentException("Tolerance runs from 5 to 300 seconds.");
        if (Pace is < 0.3 or > 3) throw new ArgumentException("Pace runs from 0.3 to 3.");
        if (Recovery is < 5 or > 180) throw new ArgumentException("Recovery runs from 5 to 180 seconds.");
        return this;
    }
}

/// <summary>What she has learned about his body across sessions. Kept apart from the settings he edits.</summary>
public sealed record LearnedProfile
{
    public double Pace { get; init; } = 1;
    public double PaceEvidence { get; init; }
    public double Recovery { get; init; } = ProfileState.DefaultRecovery;
    public double RecoveryEvidence { get; init; }
    /// <summary>How far what he has taken (or failed to) has moved his tolerance from what he set.</summary>
    public double ToleranceFactor { get; init; } = 1;

    /// <summary>What she takes away from a session.</summary>
    public static LearnedProfile From(ProfileState p, LearnedProfile before) => new()
    {
        Pace = p.PaceFixed ? before.Pace : Math.Round(p.Pace, 3),
        PaceEvidence = p.PaceFixed ? before.PaceEvidence : Math.Round(p.PaceEvidence, 1),
        Recovery = p.RecoveryFixed ? before.Recovery : Math.Round(p.Recovery, 1),
        RecoveryEvidence = p.RecoveryFixed ? before.RecoveryEvidence : Math.Round(p.RecoveryEvidence, 1),
        ToleranceFactor = p.ToleranceBase > 0 ? Math.Round(Math.Clamp(p.Tolerance / p.ToleranceBase, ProfileState.ToleranceDown, ProfileState.ToleranceUp), 3) : 1,
    };
}

/// <summary>
/// Her persona as the user sets it once: how she talks, not what she makes him do. Each dial runs
/// 0–10; she varies around them from session to session by up to <see cref="Variance"/>.
/// </summary>
public sealed record PersonaSettings
{
    /// <summary>How harsh her tone is when she is in a neutral mood.</summary>
    public int Harshness { get; init; } = 5;
    /// <summary>How much humiliation she uses in what she says.</summary>
    public int Humiliation { get; init; } = 3;
    /// <summary>Mind games: false promises, lying about the ending, doubt.</summary>
    public int Manipulation { get; init; } = 5;
    /// <summary>How much she says beyond the orders.</summary>
    public int Talkativeness { get; init; } = 5;
    /// <summary>How far she drifts from these settings from one session to the next (0–3).</summary>
    public int Variance { get; init; } = 2;
    /// <summary>What she calls him. Empty: her own choice, depending on humiliation.</summary>
    public string[] Names { get; init; } = [];

    public PersonaSettings Validate()
    {
        foreach (var dial in new[] { Harshness, Humiliation, Manipulation, Talkativeness })
            if (dial is < 0 or > 10) throw new ArgumentException("Persona settings run from 0 to 10.");
        if (Variance is < 0 or > 3) throw new ArgumentException("Variance runs from 0 to 3.");
        var names = (Names ?? []).Select(x => x?.Trim() ?? "").Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (names.Length > 12 || names.Any(x => x.Length > 40)) throw new ArgumentException("Up to 12 names of 40 characters each.");
        return this with { Names = names };
    }
}

/// <summary>
/// What the Length setting means to this user: minutes of play for a length 1, 5 and 10 session,
/// with the lengths between interpolated. Sessions still run on points (what he does, not the
/// clock); minutes become points at his pace.
/// </summary>
public sealed record SessionLengths
{
    public int Length1 { get; init; } = 10;
    public int Length5 { get; init; } = 35;
    public int Length10 { get; init; } = 90;

    public double Minutes(int length)
    {
        length = Math.Clamp(length, 1, 10);
        return length <= 5
            ? Length1 + (Length5 - Length1) * (length - 1) / 4.0
            : Length5 + (Length10 - Length5) * (length - 5) / 5.0;
    }

    public SessionLengths Validate()
    {
        if (Length1 is < 3 or > 120 || Length5 is < 5 or > 240 || Length10 is < 10 or > 480)
            throw new ArgumentException("Session lengths run from 3 minutes (length 1) to 8 hours (length 10).");
        if (!(Length1 < Length5 && Length5 < Length10)) throw new ArgumentException("Longer lengths must be longer sessions.");
        return this;
    }
}

/// <summary>When she may ruin an orgasm.</summary>
public enum RuinPreference
{
    /// <summary>Never: no ruin deals, no ruin stakes, and she never chooses one.</summary>
    Never,
    /// <summary>Only as a price or a punishment: deals he accepts, what he owes, punishment sessions.</summary>
    PunishmentOnly,
    /// <summary>As a price, a punishment, or now and then her own choice.</summary>
    Sometimes,
    /// <summary>Her own choice more often.</summary>
    Often,
}

/// <summary>How much she leans toward denial at the end of a session.</summary>
public enum DenialPreference
{
    /// <summary>She lets him cum at the end unless a promise or a denial window he agreed to says otherwise.</summary>
    Never,
    Rarely,
    Normal,
    Often,
    /// <summary>Orgasms are rare treats.</summary>
    Mostly,
}

/// <summary>
/// How he wants his sessions to end, in general. Each session's Denial setting still moves her
/// within these, and what he does still changes her mind.
/// </summary>
public sealed record EndingPreferences
{
    public DenialPreference Denial { get; init; } = DenialPreference.Normal;
    public RuinPreference Ruins { get; init; } = RuinPreference.Sometimes;
    /// <summary>Roughly how many days he wants between orgasms (0: no preference).</summary>
    public int OrgasmEveryDays { get; init; }

    public EndingPreferences Validate()
    {
        if (!Enum.IsDefined(Denial) || !Enum.IsDefined(Ruins)) throw new ArgumentException("Unknown ending preference.");
        if (OrgasmEveryDays is < 0 or > 60) throw new ArgumentException("Days between orgasms run from 0 (no preference) to 60.");
        return this;
    }
}

/// <summary>The user's settings for her: her persona, how sessions end, what a session length means, and how long he can stay at the edge.</summary>
public sealed record DommeSettings
{
    public PersonaSettings Persona { get; init; } = new();
    public EndingPreferences Endings { get; init; } = new();
    public SessionLengths Lengths { get; init; } = new();
    public ProfileSettings Profile { get; init; } = new();

    public DommeSettings Validate()
        => new() { Persona = (Persona ?? new()).Validate(), Endings = (Endings ?? new()).Validate(), Lengths = (Lengths ?? new()).Validate(), Profile = (Profile ?? new()).Validate() };
}

/// <summary>Her persona for one session: the user's settings, varied a little, and recorded with the session.</summary>
public sealed record SessionPersona
{
    public int Harshness { get; init; } = 5;
    public int Humiliation { get; init; } = 3;
    public int Manipulation { get; init; } = 5;
    public int Talkativeness { get; init; } = 5;
    public string[] Names { get; init; } = [];

    /// <summary>
    /// Tonight's persona: each dial moves around the user's setting by up to the variance, and away
    /// from where it sat last session so two sessions in a row do not feel the same. How the last
    /// session went nudges her too: a walkout or a broken promise makes her harsher and more
    /// manipulative.
    /// </summary>
    public static SessionPersona For(PersonaSettings settings, Recall recall, IRandom r)
    {
        int Vary(int value, int? last)
        {
            int spread = settings.Variance;
            int delta = spread == 0 ? 0 : r.Next(-spread, spread + 1);
            if (last is int previous && delta == 0 && spread > 0 && value + delta == previous) delta = r.Next(0, 2) == 0 ? -1 : 1;
            return Math.Clamp(value + delta, 0, 10);
        }
        var last = recall.LastPersona;
        int grudge = recall.WalkoutsRecently > 0 || recall.BrokenPromisesRecently > 0 ? 1 : 0;
        return new()
        {
            Harshness = Math.Clamp(Vary(settings.Harshness, last?.Harshness) + grudge, 0, 10),
            Humiliation = Vary(settings.Humiliation, last?.Humiliation),
            Manipulation = Math.Clamp(Vary(settings.Manipulation, last?.Manipulation) + grudge, 0, 10),
            Talkativeness = Vary(settings.Talkativeness, last?.Talkativeness),
            Names = settings.Names,
        };
    }
}

/// <summary>How often a line module has been used before, across sessions.</summary>
public sealed record ModuleUse(int Uses, DateTime LastUsedUtc, int SessionsAgo);

/// <summary>
/// What she remembers of him from before this session, read from the facts already stored:
/// sessions, outcomes, edges and methods, requests, promises, and the lines she has used.
/// </summary>
public sealed record Recall
{
    public int Sessions { get; init; }
    public double? DaysSinceLastSession { get; init; }
    public SessionOutcome? LastOutcome { get; init; }
    /// <summary>Sessions in a row that ended in denial, most recent first.</summary>
    public int DenialStreak { get; init; }
    public double? DaysSinceOrgasm { get; init; }
    public int EdgesToday { get; init; }
    public int EdgesSinceOrgasm { get; init; }
    public int LastSessionBegs { get; init; }
    public double AverageBegs { get; init; }
    public int WalkoutsRecently { get; init; }
    public int BrokenPromisesRecently { get; init; }
    /// <summary>Methods he edges on most, best first.</summary>
    public int[] FavoriteMethods { get; init; } = [];
    public string? FavoriteMethodName { get; init; }
    /// <summary>The segment types her last sessions opened with, most recent first.</summary>
    public string[] RecentOpenings { get; init; } = [];
    /// <summary>Segment types used in the last session.</summary>
    public string[] LastSegments { get; init; } = [];
    public SessionPersona? LastPersona { get; init; }
    public Dictionary<string, ModuleUse> Usage { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Points he earned per minute of play in his recent finished sessions, and how many sessions that is from.</summary>
    public double? PointsPerMinute { get; init; }
    public int PaceSessions { get; init; }
}

/// <summary>What is on screen right now, as Cove reports it.</summary>
public sealed record Screen
{
    public string Kind { get; init; } = "";
    public int Id { get; init; }
    public double? Position { get; init; }
    public string? Title { get; init; }
    public string[] Performers { get; init; } = [];
    /// <summary>Tags on the whole video.</summary>
    public string[] Tags { get; init; } = [];
    /// <summary>Tags on the part of the video playing now (time-ranged segments).</summary>
    public string[] NowTags { get; init; } = [];
    public string? NowTitle { get; init; }
    public DateTime SeenAtUtc { get; init; }
}

/// <summary>Builds the facts she speaks from.</summary>
public static class Knowledge
{
    /// <summary>A screen report older than this is not trusted as "on screen now".</summary>
    public static readonly TimeSpan ScreenFresh = TimeSpan.FromSeconds(20);

    public static Facts Facts(SessionState? s, Screen? screen, DateTime now, SessionPersona? persona = null, Recall? recall = null)
    {
        var f = new Facts();
        persona ??= s?.Persona ?? new();
        recall ??= s?.Recall ?? new();
        f.Set("harsh", (double)persona.Harshness).Set("humiliation", (double)persona.Humiliation)
         .Set("manipulation", (double)persona.Manipulation).Set("talk", (double)persona.Talkativeness);

        // Memory of earlier sessions.
        f.Set("sessions", (double)recall.Sessions).Set("denial.streak", (double)recall.DenialStreak)
         .Set("edges.today", (double)recall.EdgesToday).Set("edges.sinceorgasm", (double)recall.EdgesSinceOrgasm)
         .Set("last.begs", (double)recall.LastSessionBegs).Set("walkouts", (double)recall.WalkoutsRecently)
         .Set("broken.promises", (double)recall.BrokenPromisesRecently)
         .Set("last.outcome", recall.LastOutcome?.ToString()).Set("favorite", recall.FavoriteMethodName);
        if (recall.DaysSinceLastSession is double days) f.Set("last.days", Math.Round(days, 1));
        if (recall.DaysSinceOrgasm is double since) f.Set("orgasm.days", Math.Floor(since));

        if (screen is not null && now - screen.SeenAtUtc <= ScreenFresh)
        {
            f.Set("screen", true).Set("screen.kind", screen.Kind).Set("title", screen.Title)
             .Set("performers", screen.Performers.ToList()).Set("performer", screen.Performers.FirstOrDefault())
             .Set("tags", screen.Tags.Concat(screen.NowTags).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
             .Set("tag", screen.NowTags.FirstOrDefault() ?? screen.Tags.FirstOrDefault());
            if (screen.NowTags.Length > 0) f.Set("screen.now", true);
        }
        if (s is null) return f;

        var mood = s.Mood;
        f.Set("mood", mood.Mood.ToString()).Set("mood.intensity", mood.Intensity).Set("mood.cause", mood.Cause.ToString())
         .Set("annoyance", mood.Annoyance).Set("mode", s.Contract.Mode.ToString()).Set("punishment", s.Contract.Punishment)
         .Set("edges", (double)s.EdgesThisSession).Set("edges.step", (double)s.EdgesThisStep).Set("begs", (double)s.RequestsThisSession)
         .Set("denials", (double)s.RequestsDenied).Set("rest.begs", (double)s.RestRequests).Set("progress", Math.Round(s.Progress, 2))
         .Set("step", (double)s.StepNumber).Set("minutes", Math.Round((now - s.StartedAtUtc).TotalMinutes - s.PausedSeconds / 60, 1))
         .Set("arousal", s.Arousal).Set("retaliating", s.Retaliation.Active).Set("slipped", s.UnauthorizedOrgasms > 0).Set("owed.edges", (double)s.OwedEdges)
         .Set("owed.ruins", (double)s.OwedRuins).Set("promise.noorgasm", s.DenialUntilUtc is DateTime until && until > now)
         .Set("games.won", (double)s.GamesWon).Set("games.lost", (double)s.GamesLost).Set("challenges.failed", (double)s.ChallengesFailed)
         .Set("challenges.passed", (double)s.ChallengesPassed).Set("offers.accepted", (double)s.OffersAccepted)
         .Set("finish.decided", s.FinishDecided).Set("permission", s.PermissionGranted).Set("lube", s.LubeActive)
         .Set("user.pace", s.Contract.Pacing == Authority.User).Set("user.methods", s.Contract.Methods == Authority.User)
         .Set("user.edges", s.Contract.Edges == Authority.User)
         .Set("ending.lean", s.Ending.Kind.ToString()).Set("ending.presentation", s.Ending.Presentation.ToString());
        if (s.Contract.Obligations.Character is { } c)
        {
            var traits = new List<string>();
            if (c.Long) traits.Add("long");
            if (c.Intense) traits.Add("intense");
            if (c.Torturous) traits.Add("torturous");
            if (c.Mean) traits.Add("mean");
            if (c.Teasing) traits.Add("teasing");
            f.Set("traits", traits);
        }
        if (s.Current is { } d)
        {
            f.Set("step.kind", d.Kind.ToString()).Set("step.category", d.Category.ToString()).Set("speed", (double)d.Speed);
            if (d.Method is { IsUserChoice: false } m)
                f.Set("method", m.Name).Set("method.torture", (double)m.TortLevel).Set("method.tease", (double)m.Tease)
                 .Set("method.favorite", recall.FavoriteMethods.Contains(m.Id));
            if (s.TimerDueUtc is DateTime due) f.Set("step.left", Math.Max(0, Math.Round((due - now).TotalSeconds)));
            f.Set("step.elapsed", Math.Max(0, Math.Round((now - s.StepStartedAtUtc).TotalSeconds)));
        }
        if (s.Plan?.Current is { } segment)
        {
            f.Set("segment", segment.Type).Set("segment.steps", (double)segment.Steps);
            if (s.Plan.Index > 0) f.Set("segment.previous", s.Plan.Segments[s.Plan.Index - 1].Type);
            if (SegmentCatalogue.Default.Find(segment.Type) is { } type) f.Set("segment.tags", type.Tags.ToList());
        }
        return f;
    }
}
