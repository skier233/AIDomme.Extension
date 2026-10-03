using AIDome.Data;
using AIDome.Sessions.Plan;
using AIDome.Speech;

namespace AIDome.Sessions;

/// <summary>
/// The next step: an instruction with its timer, a game to start, or the ending. <see cref="Say"/>
/// is what she says as it begins (a new segment, a hint about the ending).
/// </summary>
public sealed record PlannedStep(Directive? Directive, int TimerSeconds, GameType? Game = null, bool Ending = false)
{
    public List<Utterance> Say { get; init; } = [];
    /// <summary>Tags for the instruction's own line (for example <c>consequence</c>).</summary>
    public string[] Tags { get; init; } = [];
}

/// <summary>
/// Decides steps within the plan: runs the current segment's step template, and builds the
/// directives the engine needs for holds, rests, endings, prices and free play. It never makes
/// text; the words come from the narrator.
/// </summary>
public interface IPlanner
{
    PlanEngine Plans { get; }
    /// <summary>The next step of the plan, a game to start, a pending consequence, or the ending.</summary>
    PlannedStep Next(SessionState state, DateTime now);
    Directive EdgeHold(SessionState state, int seconds);
    Directive Cooldown(SessionState state, int seconds);
    Directive Resume(SessionState state, Directive suspended, int remaining);
    /// <summary>Permission to finish within a window. Ruined when owed or chosen.</summary>
    Directive Finishing(SessionState state, bool ruined = false);
    Directive PunishmentEnding(SessionState state, EnderInfo ender);
    Directive PriceStep(SessionState state, Price price);
    Directive OfferDecision(SessionState state, PendingOffer offer);
    Directive Denied(SessionState state);
    Directive Rest(SessionState state, int seconds);
    /// <summary>The standing instruction when he sets the pace and the edges.</summary>
    Directive FreePlay(SessionState state, int checkSeconds);
    /// <summary>The method stroking games use.</summary>
    MethodInfo Stroke(SessionState state);
    GameContext GameContext(SessionState state, MethodInfo stroke, DateTime? now = null);
}

public sealed class SegmentPlanner(IRandom r, Authorities a) : IPlanner
{
    public PlanEngine Plans { get; } = new(r, SegmentCatalogue.Default);

    public PlannedStep Next(SessionState s, DateTime now)
    {
        if (s.PendingConsequences.Count > 0)
        {
            var consequence = s.PendingConsequences[0];
            s.PendingConsequences.RemoveAt(0);
            if (Consequence(s, consequence) is { } step) return step;
        }
        if (s.PendingLube is { } lube)
        {
            s.PendingLube = null;
            return new(lube, TimerFor(lube));
        }

        Mood.Drift(s, r);
        if (s.Retaliation.Active && --s.Retaliation.StepsLeft <= 0) s.Retaliation.Active = false;

        var plan = s.Plan ??= Plans.Make(s, a, new Dictionary<string, SegmentPreference>());
        var say = new List<Utterance>();
        if (plan.Current is not { Started: true } || Plans.Done(s)) say.AddRange(Plans.Next(s, a, now));
        if (plan.AtEnding) return new(null, 0, Ending: true) { Say = say };
        var segment = plan.Current!;
        var type = Plans.Catalogue.Find(segment.Type)!;
        var built = Build(s, type, segment);
        segment.Steps++;
        return built with { Say = say.Concat(built.Say).ToList() };
    }

    // ── Steps from a segment's template ──

    private PlannedStep Build(SessionState s, SegmentType type, PlannedSegment seg)
    {
        var tpl = type.Step;
        if (seg.Steps > 0 && tpl.Kind is not (StepKind.Rest or StepKind.TeaseRest) && r.NextDouble() < type.RestBetween)
            return Record(s, StepCategory.Rest, Rest(s, r.Next(8, 18)) with { Segment = type.Id });
        if (tpl.Kind == StepKind.Game)
            return new(null, 0, Games.Choose(GameContext(s, Stroke(s)), s.RecentGames, s.Contract.GamesOnly));

        var category = Category(type, tpl.Kind);
        var phase = Phase(type);
        int seconds() => r.Jitter(Between(tpl.Seconds));
        switch (tpl.Kind)
        {
            case StepKind.Rest:
            case StepKind.TeaseRest:
            {
                int dur = Between(tpl.Seconds);
                if (s.Contract.Punishment || s.Mean >= 8) dur = Math.Max(8, dur * 2 / 3);
                return Record(s, StepCategory.Rest, Rest(s, dur) with { Segment = type.Id, Phase = SessionPhase.Resting });
            }
        }

        // Steps that ask for edges need a method that can get him there.
        bool edging = tpl.Kind is StepKind.Edges or StepKind.Hold or StepKind.MaxEdges;
        bool hard = edging && tpl.Reach == "hard";
        var method = Method(s, tpl.Method, seg,
            hard ? MethodPreference.HardReachMin : edging ? MethodPreference.EdgeReach : 0,
            hard ? MethodPreference.HardReachMax : 10);
        int speed = Speed(s, tpl, seg, type);
        Directive d = tpl.Kind switch
        {
            StepKind.Edges or StepKind.Hold when a.Edges.DommeSets => EdgeCount(s, tpl, method, speed),
            StepKind.NoEdge => Timed(method, speed, seconds()) with { ForbidEdge = true },
            StepKind.MaxEdges => Timed(method, speed, seconds()) with { CountEdges = true },
            StepKind.Burst => Timed(method, speed, Between(tpl.Seconds)),
            // Without edge authority, edge steps are timed pushes toward the edge.
            _ => Timed(method, speed, seconds()),
        };
        return Record(s, category, d with { Category = category, Phase = phase, Segment = type.Id });
    }

    private Directive EdgeCount(SessionState s, StepTemplate tpl, MethodInfo m, int speed)
    {
        int count = tpl.Kind == StepKind.Hold ? 1 : Between(tpl.Count) + (s.Retaliation.Active || s.Contract.Punishment ? 1 : 0);
        int hold = tpl.Kind == StepKind.Hold || r.NextDouble() < tpl.HoldChance * (s.Contract.Punishment ? 1.6 : 1)
            ? (int)(Between(tpl.Hold) * (1.0 + s.Mean / 40.0))
            : 0;
        return new() { Kind = InstructionKind.EdgeCount, Method = m, Speed = speed, DurationOrCount = count, HoldSeconds = hold };
    }

    private static Directive Timed(MethodInfo m, int speed, int seconds)
        => new() { Kind = InstructionKind.TimedMethod, Method = m, Speed = speed, DurationOrCount = seconds };

    private int Speed(SessionState s, StepTemplate tpl, PlannedSegment seg, SegmentType type)
    {
        if (!a.Pacing.DommeSets) return 0;
        int lo = tpl.Speed[0], hi = tpl.Speed[1];
        if (tpl.Rise)
        {
            double at = seg.MaxSteps <= 1 ? 1 : Math.Min(1, seg.Steps / (double)(Math.Max(seg.MinSteps, Math.Min(seg.MaxSteps, 5)) - 1));
            return Math.Clamp((int)Math.Round(lo + (hi - lo) * at), 1, 10);
        }
        if ((s.Retaliation.Active || s.Mood.Annoyance > 0.6) && type.Has("torture")) lo = Math.Min(hi, lo + 1);
        return a.Pacing.Speed(r, lo, hi);
    }

    /// <summary>A method by the segment's policy, within the session's intensity plus the policy's ceiling.</summary>
    private MethodInfo Method(SessionState s, MethodPolicy policy, PlannedSegment seg, int minCloseness = 0, int maxCloseness = 10)
    {
        if (!a.Methods.DommeChooses) return MethodInfo.UserChoice(s.Methods);
        int ceiling = Math.Clamp(s.Intensity + policy.Ceiling, 2, 10);
        var preference = new MethodPreference(ceiling, Tease: policy.Prefer == "tease", Torture: policy.Prefer == "torture",
            Closeness: policy.Prefer == "closeness", Basic: policy.Prefer == "basic", MinCloseness: minCloseness, MaxCloseness: maxCloseness);
        bool Reaches(MethodInfo m) => m.Closeness >= minCloseness && m.Closeness <= maxCloseness;
        MethodInfo? fixedMethod = seg.MethodId is int id ? s.Methods.FirstOrDefault(m => m.Id == id) : null;
        // A method kept for the segment still has to fit this step's reach.
        if (fixedMethod is not null && !Reaches(fixedMethod) && s.Methods.Any(Reaches)) fixedMethod = null;
        switch (policy.Policy)
        {
            case "same" when fixedMethod is not null:
                return fixedMethod;
            case "favorite" when fixedMethod is not null:
                return fixedMethod;
            case "favorite":
            {
                var favorite = s.Recall.FavoriteMethods.Select(f => s.Methods.FirstOrDefault(m => m.Id == f && m.Intensity <= ceiling && m.Closeness >= minCloseness)).FirstOrDefault(m => m is not null);
                var chosen = favorite ?? a.Methods.Choose(s, preference);
                seg.MethodId = chosen.Id;
                return chosen;
            }
            case "hated":
            {
                if (fixedMethod is not null) return fixedMethod;
                var pool = s.Methods.Where(m => m.Intensity <= Math.Min(10, ceiling + 1) && Reaches(m)).OrderByDescending(m => m.TortLevel).Take(3).ToList();
                var chosen = pool.Count > 0 ? r.Pick(pool) : a.Methods.Choose(s, preference);
                seg.MethodId = chosen.Id;
                return chosen;
            }
            case "swap":
            {
                var last = s.RecentMethodIds.Count > 0 ? s.RecentMethodIds[^1] : -1;
                var chosen = a.Methods.Choose(s, preference);
                for (int tries = 0; tries < 3 && chosen.Id == last && s.Methods.Count > 1; tries++) chosen = a.Methods.Choose(s, preference);
                return chosen;
            }
            default:
            {
                var chosen = a.Methods.Choose(s, preference);
                if (policy.Policy == "same") seg.MethodId = chosen.Id;
                return chosen;
            }
        }
    }

    private static StepCategory Category(SegmentType type, StepKind kind) => kind switch
    {
        StepKind.Rest or StepKind.TeaseRest => StepCategory.Rest,
        StepKind.Game => StepCategory.Game,
        StepKind.Burst => StepCategory.Surprise,
        StepKind.Edges or StepKind.Hold => StepCategory.Edge,
        _ when type.Has("torture") => StepCategory.Torment,
        _ when type.Has("tease") => StepCategory.Tease,
        _ when type.Has("edging") => StepCategory.Edge,
        _ => StepCategory.Stroke,
    };

    private static SessionPhase Phase(SegmentType type)
        => type.Has("torture") ? SessionPhase.Tormenting
            : type.Has("rest") ? SessionPhase.Resting
            : type.Has("tease") ? SessionPhase.Teasing
            : type.Has("edging") ? SessionPhase.Edging
            : type.Has("intense") ? SessionPhase.Intense
            : SessionPhase.Building;

    /// <summary>Bookkeeping for a planned step, including the lube pause before a lubed method.</summary>
    private PlannedStep Record(SessionState s, StepCategory category, Directive directive)
    {
        s.RecentIntensities.Add(directive.Method?.Intensity ?? 0);
        if (s.RecentIntensities.Count > 5) s.RecentIntensities.RemoveAt(0);
        s.StepsSinceRest = directive.Kind == InstructionKind.Rest ? 0 : s.StepsSinceRest + 1;
        s.PreviousCategory = category;
        if (a.Methods.DommeChooses && !s.LubeActive && directive.Method?.RequiresLube == true)
        {
            s.PendingLube = directive;
            s.LubeActive = true;
            return new(new Directive { Kind = InstructionKind.FreeText, Category = StepCategory.Rest, Phase = SessionPhase.Resting, Segment = directive.Segment }, 0)
            {
                Tags = ["lube"],
            };
        }
        return new(directive, TimerFor(directive));
    }

    public static int TimerFor(Directive d)
        => d.Kind is InstructionKind.TimedMethod or InstructionKind.Rest or InstructionKind.FreeText && d.DurationOrCount > 0 ? d.DurationOrCount : 0;

    /// <summary>A game stake that becomes a step. Points and odds are applied by the engine directly.</summary>
    private PlannedStep? Consequence(SessionState s, Consequence c)
    {
        switch (c.Kind)
        {
            case ConsequenceKind.Rest:
                return new(Rest(s, c.Amount), c.Amount) { Tags = ["reward"] };
            case ConsequenceKind.ExtraEdges when a.Edges.DommeSets:
            {
                var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 1), Closeness: true, Basic: true, MinCloseness: MethodPreference.EdgeReach));
                var d = new Directive
                {
                    Kind = InstructionKind.EdgeCount, Category = StepCategory.Edge, Method = m, Speed = a.Pacing.Speed(r, 5, 9),
                    DurationOrCount = c.Amount, HoldSeconds = c.Hold, Phase = SessionPhase.Edging,
                };
                return new(d, 0) { Tags = ["consequence"] };
            }
            case ConsequenceKind.ExtraEdges:
            case ConsequenceKind.Torment:
            {
                int seconds = c.Kind == ConsequenceKind.Torment ? c.Amount : c.Amount * 20;
                var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 2), Torture: true));
                var d = new Directive
                {
                    Kind = InstructionKind.TimedMethod, Category = StepCategory.Torment, Method = m, Speed = a.Pacing.Speed(r, 6, 9),
                    DurationOrCount = seconds, Phase = SessionPhase.Tormenting,
                };
                return new(d, seconds) { Tags = ["consequence"] };
            }
            default:
                return null;
        }
    }

    // ── Directives the engine asks for ──

    public Directive EdgeHold(SessionState s, int seconds) => new()
    {
        Kind = InstructionKind.FreeText, Category = StepCategory.Hold, DurationOrCount = seconds, Phase = SessionPhase.Edging, Segment = s.Current?.Segment,
    };

    public Directive Cooldown(SessionState s, int seconds) => new()
    {
        Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = seconds, Phase = SessionPhase.Resting, Segment = s.Current?.Segment,
    };

    public Directive Resume(SessionState s, Directive suspended, int remaining) => suspended with { DurationOrCount = remaining };

    public Directive Rest(SessionState s, int seconds) => new()
    {
        Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = seconds, Phase = SessionPhase.Resting,
    };

    public Directive Finishing(SessionState s, bool ruined = false)
    {
        var m = a.Methods.Choose(s, new(s.Intensity, Basic: true));
        return new()
        {
            Kind = InstructionKind.TimedMethod, Category = StepCategory.Finish, Method = m, Speed = a.Pacing.Speed(r, 5, 9),
            DurationOrCount = r.Next(30, 90), Phase = SessionPhase.Finishing,
        };
    }

    public Directive PunishmentEnding(SessionState s, EnderInfo ender) => ender.EndsInOrgasm
        ? new() { Kind = InstructionKind.TimedMethod, Category = StepCategory.Finish, DurationOrCount = 180, Phase = SessionPhase.Finishing }
        : new() { Kind = InstructionKind.Finish, Category = StepCategory.Finish, Phase = SessionPhase.Finishing };

    public Directive PriceStep(SessionState s, Price price)
    {
        if (!a.Edges.DommeSets)
            return new()
            {
                Kind = InstructionKind.FreeText, Category = a.PlansSteps ? StepCategory.Edge : StepCategory.FreePlay, Method = MethodInfo.UserChoice(s.Methods),
                Phase = SessionPhase.Edging, HiddenTimer = !a.PlansSteps, DurationOrCount = price.EdgesLeft, HoldSeconds = price.Hold,
            };
        var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 1), Closeness: true, Basic: true, MinCloseness: MethodPreference.EdgeReach));
        return new()
        {
            Kind = InstructionKind.EdgeCount, Category = StepCategory.Edge, Method = m, Speed = a.Pacing.Speed(r, 5, 9),
            DurationOrCount = price.EdgesLeft, HoldSeconds = price.Hold, Phase = SessionPhase.Edging,
        };
    }

    public Directive OfferDecision(SessionState s, PendingOffer offer) => new()
    {
        Kind = InstructionKind.FreeText, Category = StepCategory.Offer, DurationOrCount = Offers.ExpirySeconds, Phase = SessionPhase.Resting,
    };

    public Directive Denied(SessionState s) => new() { Kind = InstructionKind.Finish, Category = StepCategory.Finish, Phase = SessionPhase.Finishing };

    public Directive FreePlay(SessionState s, int checkSeconds) => new()
    {
        Kind = InstructionKind.FreeText, Category = StepCategory.FreePlay, Method = MethodInfo.UserChoice(s.Methods),
        Phase = SessionPhase.Building, DurationOrCount = checkSeconds, HiddenTimer = true,
    };

    /// <summary>A plain grip for games, most of which end at the edge, so it must get him there.</summary>
    public MethodInfo Stroke(SessionState s) => a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 1), Basic: true, MinCloseness: MethodPreference.EdgeReach));

    public GameContext GameContext(SessionState s, MethodInfo stroke, DateTime? now = null)
        => new(s.Mean, s.Intensity, s.Arousal, s.Mood.Mood, stroke, a.Edges.DommeSets, s.EdgesThisStep, now ?? s.LastActivityUtc, r);

    private int Between(int[] range) => r.Next(range[0], range[1] + 1);
}

/// <summary>
/// Her mood: it lasts several steps, drifts with the session, reacts to what he does, and always
/// has a cause she can name. Annoyance is separate: it builds when he begs or disobeys and fades
/// when he obeys, and makes the rest of the plan harder.
/// </summary>
public static class Mood
{
    public static void Start(SessionState s, IRandom r)
    {
        var persona = s.Persona;
        var recall = s.Recall;
        double harsh = persona.Harshness / 10.0;
        if (s.Contract.Punishment)
            s.Mood.Mood = r.Pick([DommeMood.Cruel, DommeMood.Relentless, DommeMood.Edgy]);
        else
            s.Mood.Mood = r.Weighted<DommeMood>(
            [
                (DommeMood.Neutral, 30), (DommeMood.Teasing, 18 * (1.3 - harsh)), (DommeMood.Playful, 12 * (1.3 - harsh)),
                (DommeMood.Edgy, 15), (DommeMood.Relentless, 10 + 8 * harsh), (DommeMood.Cruel, 6 + 14 * harsh),
            ], DommeMood.Neutral);
        s.Mood.Cause = MoodCause.Temperament;
        s.Mood.StepsRemaining = 4 + r.Next(0, 5);
        s.Mood.Intensity = 0.4 + r.NextDouble() * 0.4;
        // How the last session went colours how she starts this one.
        if (recall.WalkoutsRecently > 0 || recall.BrokenPromisesRecently > 0 || recall.LastOutcome is SessionOutcome.UnauthorizedOrgasm or SessionOutcome.Abandoned)
        {
            s.Mood.Annoyance = 0.4;
            s.Mood.Cause = MoodCause.LastSession;
            if (s.Mood.Mood is DommeMood.Playful or DommeMood.Teasing or DommeMood.Neutral) s.Mood.Mood = DommeMood.Cruel;
        }
    }

    public static void Drift(SessionState s, IRandom r)
    {
        // Obedience softens her a little every step.
        s.Mood.Annoyance = Math.Max(0, s.Mood.Annoyance - 0.03);
        if (--s.Mood.StepsRemaining > 0) return;
        double progress = s.Progress;
        var weights = new Dictionary<DommeMood, double>
        {
            [DommeMood.Neutral] = 15, [DommeMood.Cruel] = 5 + s.Mean * 2 + s.Mood.Annoyance * 20, [DommeMood.Edgy] = 15 + (s.Arousal > 50 ? 15 : 0),
            [DommeMood.Teasing] = 15 + (progress < 0.3 ? 10 : 0), [DommeMood.Playful] = 8 * (1 - s.Mood.Annoyance), [DommeMood.Relentless] = 10 + (progress > 0.5 ? 10 : 0),
        };
        weights[s.Mood.Mood] *= s.Mood.Mood is DommeMood.Neutral or DommeMood.Playful ? 0.3 : 0.4;
        if (s.Contract.Punishment)
        {
            weights[DommeMood.Teasing] *= 0.3; weights[DommeMood.Neutral] *= 0.3; weights[DommeMood.Playful] *= 0.2; weights[DommeMood.Cruel] *= 2.0;
        }
        var next = r.Weighted(weights.Select(x => (x.Key, x.Value)).ToList(), DommeMood.Neutral);
        if (next != s.Mood.Mood) s.Mood.Cause = MoodCause.Drift;
        s.Mood.Mood = next;
        s.Mood.StepsRemaining = 4 + r.Next(0, 6);
        s.Mood.Intensity = 0.4 + r.NextDouble() * 0.5;
    }

    /// <summary>Positive delta: angrier. Negative: softer. Strong reactions can flip her mood.</summary>
    public static void Nudge(SessionState s, IRandom r, double delta, MoodCause cause = MoodCause.None)
    {
        s.Mood.Intensity = Math.Clamp(s.Mood.Intensity + delta * 0.15, 0.1, 1.0);
        s.Mood.Annoyance = Math.Clamp(s.Mood.Annoyance + delta * 0.2, 0, 1);
        if (cause != MoodCause.None) s.Mood.Cause = cause;
        if (delta > 0.5 && s.Mood.Mood is DommeMood.Neutral or DommeMood.Teasing or DommeMood.Playful && r.Chance(40))
        {
            s.Mood.Mood = r.Next(0, 2) == 0 ? DommeMood.Cruel : DommeMood.Relentless;
            s.Mood.StepsRemaining = 3 + r.Next(0, 4);
            s.Mood.Intensity = Math.Max(s.Mood.Intensity, 0.6);
        }
        if (delta < -0.5 && s.Mood.Mood == DommeMood.Cruel && r.Chance(30))
        {
            s.Mood.Mood = r.Next(0, 2) == 0 ? DommeMood.Edgy : DommeMood.Neutral;
            s.Mood.StepsRemaining = 3 + r.Next(0, 3);
        }
    }

    /// <summary>Harsher steps for a while, with words that name the reason.</summary>
    public static void Retaliate(SessionState s, IRandom r, RetaliationReason reason, int minSteps, int maxStepsExclusive, double moodDelta)
    {
        s.Retaliation.Active = true;
        s.Retaliation.Reason = reason;
        s.Retaliation.StepsLeft = r.Next(minSteps, maxStepsExclusive);
        var cause = reason switch
        {
            RetaliationReason.BegCumDenied or RetaliationReason.BegStopDenied => s.RequestsThisSession + s.RestRequests >= 3 ? MoodCause.Pestering : MoodCause.Begging,
            RetaliationReason.UnauthorizedOrgasm => MoodCause.UnauthorizedOrgasm,
            RetaliationReason.LostGame => MoodCause.LostGame,
            RetaliationReason.FailedChallenge => MoodCause.FailedChallenge,
            _ => MoodCause.Disobedience,
        };
        Nudge(s, r, moodDelta, cause);
    }
}
