using AIDome.Data;
using AIDome.Services;

namespace AIDome.Sessions;

/// <summary>The next step: an instruction with its timer, or a game to start.</summary>
public sealed record PlannedStep(Directive? Directive, int TimerSeconds, GameType? Game = null);

/// <summary>
/// The rule-based planner: which kind of step comes next, and its method, speed, duration or edge
/// target and wording. It only ever prescribes what the contract lets the domme decide. This is the
/// seam a richer planner (phases, or a language model) replaces; the engine around it stays.
/// </summary>
public interface IPlanner
{
    /// <summary>The next step, a game to start, or a pending consequence.</summary>
    PlannedStep Next(SessionState state);
    /// <summary>Hold at the edge for a number of seconds.</summary>
    Directive EdgeHold(SessionState state, int seconds);
    /// <summary>Hands off briefly after an edge during a timed step.</summary>
    Directive Cooldown(SessionState state, int seconds);
    /// <summary>Back to an interrupted timed step for its remaining seconds.</summary>
    Directive Resume(SessionState state, Directive suspended, int remaining);
    /// <summary>Permission to finish within a window: her own decision, or a granted request.</summary>
    Directive Finishing(SessionState state, bool requested);
    /// <summary>Her denial.</summary>
    Directive Denied(SessionState state, string? text = null);
    /// <summary>The standing instruction when the user sets the pace and the edges.</summary>
    Directive FreePlay(SessionState state, int checkSeconds);
    /// <summary>Her last words when the session ends.</summary>
    string EndLine(SessionState state, bool orgasmGranted);
    /// <summary>The method stroking games use.</summary>
    MethodInfo Stroke(SessionState state);
    GameContext GameContext(SessionState state, MethodInfo stroke, DateTime? now = null);
}

public sealed class RulePlanner(IRandom r, Authorities a) : IPlanner
{
    private const int MinHoldNormal = 5, MaxHoldNormal = 20, MinHoldPunishment = 8, MaxHoldPunishment = 25;

    public PlannedStep Next(SessionState s)
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

        s.Arousal = Math.Max(0, s.Arousal - 2);
        Mood.Drift(s, r);
        if (s.Retaliation.Active && --s.Retaliation.StepsLeft <= 0) s.Retaliation.Active = false;

        if (s.FalsePromiseActive && --s.FalsePromiseStepsLeft <= 0)
        {
            s.FalsePromiseActive = false;
            return Record(s, StepCategory.Psych, FalsePromiseReveal(s));
        }

        var category = ChooseCategory(s);
        if (category == StepCategory.Game)
        {
            s.PreviousCategory = category;
            s.ChainStepsRemaining = 0;
            return new(null, 0, Games.Choose(GameContext(s, Stroke(s)), s.RecentGames, s.Contract.GamesOnly));
        }
        var directive = category switch
        {
            StepCategory.Tease => BuildTease(s),
            StepCategory.Edge => BuildEdge(s),
            StepCategory.Rest => BuildRest(s),
            StepCategory.Torment => BuildTorment(s),
            StepCategory.Surprise => BuildSurprise(s),
            StepCategory.Psych => BuildPsych(s),
            _ => BuildStroke(s),
        };
        return Record(s, category, directive);
    }

    /// <summary>Common bookkeeping for a planned step, including the lube pause before a lubed method.</summary>
    private PlannedStep Record(SessionState s, StepCategory category, Directive directive)
    {
        s.RecentIntensities.Add(directive.Method?.Intensity ?? 0);
        if (s.RecentIntensities.Count > 5) s.RecentIntensities.RemoveAt(0);
        s.StepsSinceRest = directive.Kind == InstructionKind.Rest ? 0 : s.StepsSinceRest + 1;
        s.PreviousCategory = category;

        // Staying on a good basic grip for a few steps is deliberate; teasing and rests break the chain.
        if (a.Methods.DommeChooses && directive.Method is { Closeness: >= 3, Tease: < 6 } m && category is StepCategory.Stroke or StepCategory.Edge)
        {
            if (s.ChainStepsRemaining <= 0 || s.ChainMethodId != m.Id)
            {
                s.ChainMethodId = m.Id;
                s.ChainStepsRemaining = 1 + r.Next(0, 2);
            }
        }
        else if (category is StepCategory.Rest or StepCategory.Torment or StepCategory.Tease) s.ChainStepsRemaining = 0;

        if (a.Methods.DommeChooses && !s.LubeActive && directive.Method?.RequiresLube == true)
        {
            s.PendingLube = directive;
            s.LubeActive = true;
            return new(new Directive
            {
                Kind = InstructionKind.FreeText, Category = StepCategory.Rest, Phase = SessionPhase.Resting,
                Text = "Apply lube now. Tell me when you're ready.",
            }, 0);
        }
        return new(directive, TimerFor(directive));
    }

    public static int TimerFor(Directive d)
        => d.Kind is InstructionKind.TimedMethod or InstructionKind.Rest or InstructionKind.FreeText && d.DurationOrCount > 0 ? d.DurationOrCount : 0;

    private StepCategory ChooseCategory(SessionState s)
    {
        if (s.Contract.GamesOnly) return StepCategory.Game;
        // With the user pacing, the domme only sets edge targets and holds.
        if (!a.Pacing.DommeSets) return StepCategory.Edge;

        bool punishment = s.Contract.Punishment;
        int mean = s.Mean;
        double progress = s.Progress, arousal = s.Arousal;
        double recentAvg = s.RecentIntensities.Count > 0 ? s.RecentIntensities.Average() : 0;
        double wTease, wStroke, wEdge, wRest, wTorment, wSurprise, wGame, wPsych;
        if (punishment) (wTease, wStroke, wEdge, wRest, wTorment, wSurprise, wGame, wPsych) = (5, 10, 35, 5, 35, 10, 5, 3);
        else (wTease, wStroke, wEdge, wRest, wTorment, wSurprise, wGame, wPsych) = (15, 30, 20, 15, 10, 10, 8, 5);

        if (arousal > 80) { wRest *= 2.5; wEdge *= 0.4; wTease *= 2.0; }
        else if (arousal > 55) wEdge *= 1.8;
        else if (arousal < 25) { wTease *= 1.5; wStroke *= 1.5; wEdge *= 0.2; wRest *= 0.3; }

        double meanFactor = mean / 5.0;
        wTorment *= meanFactor; wRest *= Math.Max(0.2, 2.0 - meanFactor); wSurprise *= meanFactor;
        wGame *= Math.Max(0.5, meanFactor); wPsych *= meanFactor;

        if (s.StepsSinceRest >= 5) wRest *= 2.0;
        if (s.StepsSinceRest >= 7) wRest *= 2.0;
        if (s.Current?.Kind == InstructionKind.Rest) wRest = 0;
        if (s.Current?.Kind == InstructionKind.FreeText) wSurprise = 0;
        if (recentAvg > 7 && mean < 6) { wRest *= 1.5; wTease *= 1.5; }
        if (mean >= 7 && recentAvg > 6) wTorment *= 1.5;
        if (progress > 0.4) wGame *= 1.5;
        if (progress > 0.6) wPsych *= 2.0;
        if (progress > 0.7) wEdge *= 2.0;
        if (progress > 0.9) { wEdge *= 1.5; wTorment *= 1.3; }
        if (s.Retaliation.Active) { wTorment *= 4.0; wEdge *= 3.0; wRest = 1.0; wTease *= 0.1; wSurprise *= 0.5; wGame *= 0.2; wPsych *= 0.3; }

        double mi = s.Mood.Intensity;
        switch (s.Mood.Mood)
        {
            case DommeMood.Cruel:
                wTorment *= 1 + 2.5 * mi; wEdge *= 1 + 1.5 * mi; wRest *= Math.Max(0.1, 1 - 0.6 * mi);
                wTease *= Math.Max(0.2, 1 - 0.5 * mi); wGame *= Math.Max(0.3, 1 - 0.3 * mi); break;
            case DommeMood.Edgy:
                wEdge *= 1 + 3.0 * mi; wStroke *= 1 + mi; wTorment *= Math.Max(0.3, 1 - 0.5 * mi);
                wTease *= Math.Max(0.3, 1 - 0.4 * mi); wSurprise *= Math.Max(0.3, 1 - 0.3 * mi); break;
            case DommeMood.Teasing:
                wTease *= 1 + 3.0 * mi; wStroke *= 1 + 0.5 * mi; wEdge *= Math.Max(0.3, 1 - 0.5 * mi);
                wTorment *= Math.Max(0.2, 1 - 0.6 * mi); wPsych *= 1 + mi; break;
            case DommeMood.Playful:
                wGame *= 1 + 3.0 * mi; wSurprise *= 1 + 2.0 * mi; wPsych *= 1 + 1.5 * mi; wTorment *= Math.Max(0.3, 1 - 0.4 * mi); break;
            case DommeMood.Relentless:
                wStroke *= 1 + 2.0 * mi; wEdge *= 1 + 2.0 * mi; wRest *= Math.Max(0.05, 1 - 0.8 * mi);
                wTease *= Math.Max(0.2, 1 - 0.6 * mi); wGame *= Math.Max(0.2, 1 - 0.5 * mi); wSurprise *= Math.Max(0.3, 1 - 0.4 * mi); break;
        }

        // Flow: what just happened makes some follow-ups more natural than others.
        switch (s.PreviousCategory)
        {
            case StepCategory.Tease: wStroke *= 2.5; wTease *= 0.5; wSurprise *= 0.3; wGame *= 0.3; break;
            case StepCategory.Stroke: wStroke *= 1.8; wEdge *= 2.0; wTease *= 0.4; wSurprise *= 0.4; wGame *= 0.3; break;
            case StepCategory.Edge: wRest *= 2.0; wStroke *= 1.5; wEdge *= 1.3; wTease *= 0.3; wSurprise *= 0.3; wGame *= 0.4; break;
            case StepCategory.Rest: wStroke *= 2.0; wTease *= 1.5; wEdge *= 0.3; wGame *= 2.0; wPsych *= 2.0; wSurprise *= 1.5; break;
            case StepCategory.Torment: wRest *= 2.5; wStroke *= 1.3; wTorment *= 0.4; wSurprise *= 0.3; wGame *= 0.3; break;
            case StepCategory.Surprise: wStroke *= 2.0; wEdge *= 1.5; wSurprise *= 0.1; wGame *= 0.3; break;
            case StepCategory.Game: wStroke *= 2.0; wEdge *= 1.3; wGame *= 0.1; wSurprise *= 0.3; break;
            case StepCategory.Psych: wStroke *= 1.8; wEdge *= 1.5; wPsych *= 0.2; wSurprise *= 0.3; break;
        }

        if (s.StepNumber <= 2 && !punishment) { wStroke *= 3.0; wTease *= 1.5; wEdge *= 0.2; wTorment *= 0.2; wSurprise = 0; wGame = 0; wPsych = 0; }
        else if (s.StepNumber <= 1 && punishment) { wStroke *= 1.5; wTease *= 1.5; wSurprise = 0; wGame = 0; wPsych = 0; }
        if (!a.Games.MayPlay(s)) wGame = 0;

        return r.Weighted<StepCategory>(
        [
            (StepCategory.Tease, wTease), (StepCategory.Stroke, wStroke), (StepCategory.Edge, wEdge), (StepCategory.Rest, wRest),
            (StepCategory.Torment, wTorment), (StepCategory.Surprise, wSurprise), (StepCategory.Game, wGame), (StepCategory.Psych, wPsych),
        ], StepCategory.Stroke);
    }

    // ── Step builders ──

    private Directive BuildTease(SessionState s)
    {
        var m = a.Methods.Choose(s, new(4, Tease: true));
        int speed = a.Pacing.Speed(r, 1, 3);
        if (a.Edges.DommeSets && (m.Closeness >= 4 || !a.Pacing.DommeSets))
            return EdgeCount(s, StepCategory.Tease, m, speed, 1, 0, SessionPhase.Teasing, DommeVoice.ComposeTease);
        return Timed(s, StepCategory.Tease, m, speed, r.Next(30, 90), SessionPhase.Teasing, DommeVoice.ComposeTease);
    }

    private Directive BuildStroke(SessionState s)
    {
        int ceiling = Math.Clamp((int)(s.Intensity * (s.Arousal / 100.0 + 0.5)), 2, 10);
        var m = a.Methods.Choose(s, new(ceiling, Basic: true));
        int speed = a.Pacing.Speed(r, 3, ceiling);
        bool timed = (m.TortLevel >= 5 && m.Closeness <= 3) || (m.Tease >= 6 && m.Closeness <= 3);
        if (!a.Edges.DommeSets || (timed && a.Pacing.DommeSets))
            return Timed(s, StepCategory.Stroke, m, speed, r.Jitter(r.Next(45, 150)), SessionPhase.Building, DommeVoice.ComposeStroke);
        int target = s.Contract.Punishment ? 2 + r.Next(0, 3) : 1 + r.Next(0, 2);
        return EdgeCount(s, StepCategory.Stroke, m, speed, target, 0, SessionPhase.Building, DommeVoice.ComposeStroke);
    }

    private Directive BuildEdge(SessionState s)
    {
        int ceiling = Math.Min(10, s.Intensity + 1);
        var m = a.Methods.Choose(s, new(ceiling, Closeness: true, Basic: true));
        if (a.Methods.DommeChooses && m.Closeness < 3)
        {
            var retry = a.Methods.Choose(s, new(ceiling, Closeness: true, Basic: true));
            if (retry.Closeness > m.Closeness) m = retry;
        }
        int speed = a.Pacing.Speed(r, 4, ceiling + 1);
        // Without edge authority, "edge" steps are timed pushes toward the edge.
        if (!a.Edges.DommeSets || (a.Methods.DommeChooses && m.Closeness < 2 && a.Pacing.DommeSets))
            return Timed(s, StepCategory.Edge, m, speed, r.Jitter(r.Next(30, 90)), SessionPhase.Edging, DommeVoice.ComposeEdge);
        int target = s.Retaliation.Active || s.Contract.Punishment ? 3 + r.Next(0, 4) : 1 + r.Next(0, 3);
        return EdgeCount(s, StepCategory.Edge, m, speed, target, HoldSeconds(s), SessionPhase.Edging, DommeVoice.ComposeEdge);
    }

    private Directive BuildRest(SessionState s)
    {
        int mean = s.Mean;
        int dur = (mean >= 7 || s.Contract.Punishment ? 10 : 20) + r.Next(5, 20);
        dur = Math.Max(5, (int)(dur * (1.5 - mean / 10.0)));
        if (s.Contract.Punishment) dur = Math.Max(5, dur / 2);
        return new()
        {
            Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = dur, Phase = SessionPhase.Resting,
            Text = DommeVoice.ComposeRest(Speech(s, StepCategory.Rest, null, 0, dur)),
        };
    }

    private Directive BuildTorment(SessionState s)
    {
        var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 3), Torture: true));
        int speed = a.Pacing.Speed(r, 6, 9);
        int dur = r.Jitter(s.Contract.Punishment ? r.Next(45, 120) : r.Next(30, 90));
        return Timed(s, StepCategory.Torment, m, speed, dur, SessionPhase.Tormenting, DommeVoice.ComposeTorment);
    }

    private Directive BuildSurprise(SessionState s)
    {
        var ctx = Speech(s, StepCategory.Surprise);
        switch (r.Next(0, 3))
        {
            case 0:
            {
                int dur = r.Next(10, 25);
                return new()
                {
                    Kind = InstructionKind.FreeText, Category = StepCategory.Surprise, DurationOrCount = dur, Phase = SessionPhase.Intense,
                    Text = DommeVoice.ComposeSurpriseStop(ctx, dur),
                };
            }
            case 1:
            {
                var m = a.Methods.Choose(s, new(10));
                int dur = r.Next(10, 25);
                return new()
                {
                    Kind = InstructionKind.TimedMethod, Category = StepCategory.Surprise, Method = m, Speed = a.Pacing.Speed(r, 8, 10),
                    DurationOrCount = dur, Phase = SessionPhase.Intense, Text = DommeVoice.ComposeSurpriseBurst(ctx, m, dur),
                };
            }
            default:
            {
                var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 3), Torture: true));
                int dur = r.Next(30, 60), speed = a.Pacing.Speed(r, 6, 9);
                return new()
                {
                    Kind = InstructionKind.TimedMethod, Category = StepCategory.Surprise, Method = m, Speed = speed, DurationOrCount = dur,
                    Phase = SessionPhase.Intense, Text = DommeVoice.ComposeSurpriseSwap(ctx, m, speed, dur),
                };
            }
        }
    }

    private Directive BuildPsych(SessionState s)
    {
        int roll = r.Next(0, 10);
        if (roll < 5 && !s.FalsePromiseActive && s.Arousal > 40)
        {
            s.FalsePromiseActive = true;
            s.FalsePromiseStepsLeft = 2 + r.Next(0, 3);
            return BuildStroke(s);
        }
        var m = a.Methods.Choose(s, new(s.Intensity, Basic: true));
        int dur = r.Jitter(r.Next(30, 60)), speed = a.Pacing.Speed(r, 3, 6);
        var ctx = Speech(s, StepCategory.Psych, m, speed, dur);
        return new()
        {
            Kind = InstructionKind.TimedMethod, Category = StepCategory.Psych, Method = m, Speed = speed, DurationOrCount = dur, Phase = SessionPhase.Building,
            Text = roll < 8 ? DommeVoice.ComposePsychLoyalty(ctx, m, speed, dur) : DommeVoice.ComposePsychDenialTaunt(ctx, m, speed, dur),
        };
    }

    private Directive FalsePromiseReveal(SessionState s)
    {
        var m = a.Methods.Choose(s, new(s.Intensity, Torture: true));
        int extra = r.Jitter(r.Next(60, 180)), speed = a.Pacing.Speed(r, 5, 8);
        Mood.Retaliate(s, r, RetaliationReason.BegCumDenied, 2, 5, 0);
        return new()
        {
            Kind = InstructionKind.TimedMethod, Category = StepCategory.Psych, Method = m, Speed = speed, DurationOrCount = extra,
            Phase = SessionPhase.Tormenting, Text = DommeVoice.ComposeFalsePromiseReveal(Speech(s, StepCategory.Psych, m, speed, extra), m, extra),
        };
    }

    /// <summary>A game stake that becomes a step. Points and odds are applied by the engine directly.</summary>
    private PlannedStep? Consequence(SessionState s, Consequence c)
    {
        switch (c.Kind)
        {
            case ConsequenceKind.Rest:
                var rest = new Directive
                {
                    Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = c.Amount, Phase = SessionPhase.Resting,
                    Text = $"Your reward: hands off for {c.Amount} seconds.",
                };
                return new(rest, c.Amount);
            case ConsequenceKind.ExtraEdges when a.Edges.DommeSets:
            {
                var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 1), Closeness: true, Basic: true));
                int speed = a.Pacing.Speed(r, 5, 9);
                var d = EdgeCount(s, StepCategory.Edge, m, speed, c.Amount, c.Hold, SessionPhase.Edging, DommeVoice.ComposeEdge);
                return new(d with { Text = $"Now pay up. {Games.Stroke(m)}, {EdgeWords(c.Amount, c.Hold)}." }, 0);
            }
            case ConsequenceKind.ExtraEdges:
            case ConsequenceKind.Torment:
            {
                int seconds = c.Kind == ConsequenceKind.Torment ? c.Amount : c.Amount * 20;
                var m = a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 2), Torture: true));
                int speed = a.Pacing.Speed(r, 6, 9);
                var d = Timed(s, StepCategory.Torment, m, speed, seconds, SessionPhase.Tormenting, DommeVoice.ComposeTorment);
                return new(d with { Text = $"Now pay up. {Games.Stroke(m)}{(speed > 0 ? ", fast" : "")}, for {seconds} seconds." }, seconds);
            }
            default:
                return null;
        }
    }

    private static string EdgeWords(int count, int hold)
        => (count == 1 ? "one edge" : $"{count} edges") + (hold > 0 ? $", holding each for {hold} seconds" : "");

    /// <summary>Hold at the edge after hitting it during a timed step.</summary>
    public Directive EdgeHold(SessionState s, int seconds) => new()
    {
        Kind = InstructionKind.FreeText, Category = StepCategory.Hold, DurationOrCount = seconds, Phase = SessionPhase.Edging,
        Text = DommeVoice.ComposeEdgeHold(Speech(s, StepCategory.Edge, s.Current?.Method, s.Current?.Speed ?? 0, seconds), seconds),
    };

    /// <summary>Hands off briefly after an edge, then the interrupted step resumes.</summary>
    public Directive Cooldown(SessionState s, int seconds) => new()
    {
        Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = seconds, Phase = SessionPhase.Resting,
        Text = DommeVoice.ComposeCooldown(Speech(s, StepCategory.Surprise, null, 0, seconds), seconds),
    };

    public Directive Resume(SessionState s, Directive suspended, int remaining) => suspended with
    {
        DurationOrCount = remaining,
        Text = DommeVoice.ComposeResume(Speech(s, StepCategory.Stroke, suspended.Method, suspended.Speed, remaining),
            suspended.Method ?? MethodInfo.UserChoice(s.Methods), suspended.Speed, remaining),
    };

    /// <summary>Permission to finish within a window. Proactive (her decision) or granted on request.</summary>
    public Directive Finishing(SessionState s, bool requested)
    {
        var m = a.Methods.Choose(s, new(s.Intensity, Basic: true));
        int dur = r.Next(30, 90), speed = a.Pacing.Speed(r, 5, 9);
        var text = requested
            ? $"{DommeText.Pick(DommeText.BegCumGranted)} {Games.Stroke(m)}{(speed > 0 ? $", {(speed >= 8 ? "fast" : "steady")}" : "")}. You have {dur} seconds. Record it when you cum."
            : DommeVoice.ComposeAutoFinishGranted(Speech(s, StepCategory.Stroke, m, speed, dur), m, dur);
        return new()
        {
            Kind = InstructionKind.TimedMethod, Category = StepCategory.Finish, Method = m, Speed = speed, DurationOrCount = dur,
            Phase = SessionPhase.Finishing, Text = text,
        };
    }

    public Directive Denied(SessionState s, string? text = null) => new()
    {
        Kind = InstructionKind.Finish, Category = StepCategory.Finish, Phase = SessionPhase.Finishing,
        Text = text ?? DommeVoice.ComposeAutoFinishDenied(Speech(s, StepCategory.Stroke)),
    };

    /// <summary>Free play: the user does as they like; she checks in quietly and answers requests.</summary>
    public Directive FreePlay(SessionState s, int checkSeconds)
    {
        var lines = s.Mood.Mood switch
        {
            DommeMood.Cruel => new[] { "Go on then. Do it your way. Every edge gets reported to me, and you don't cum until I say." },
            DommeMood.Teasing => ["Play however you like. I'll be watching the edges pile up. Ask nicely when you can't take it anymore."],
            DommeMood.Playful => ["Your hands, your pace. Keep me posted on every edge, and ask me when you're desperate."],
            _ => ["Do whatever you like. Record every edge. When you want to cum, you ask me first."],
        };
        return new()
        {
            Kind = InstructionKind.FreeText, Category = StepCategory.FreePlay, Method = MethodInfo.UserChoice(s.Methods),
            Phase = SessionPhase.Building, DurationOrCount = checkSeconds, HiddenTimer = true,
            Text = s.Retaliation.Active ? $"{DommeText.PickRetaliation(s.Retaliation.Reason)} {r.Pick(lines)}" : r.Pick(lines),
        };
    }

    public string EndLine(SessionState s, bool orgasmGranted) => DommeVoice.ComposeSessionEnd(Speech(s, StepCategory.Stroke), orgasmGranted);

    private Directive Timed(SessionState s, StepCategory category, MethodInfo m, int speed, int seconds, SessionPhase phase,
        Func<DommeVoice.SpeechContext, string> compose) => new()
    {
        Kind = InstructionKind.TimedMethod, Category = category, Method = m, Speed = speed, DurationOrCount = seconds, Phase = phase,
        Text = compose(Speech(s, category, m, speed, seconds)),
    };

    private Directive EdgeCount(SessionState s, StepCategory category, MethodInfo m, int speed, int target, int hold, SessionPhase phase,
        Func<DommeVoice.SpeechContext, string> compose) => new()
    {
        Kind = InstructionKind.EdgeCount, Category = category, Method = m, Speed = speed, DurationOrCount = target, HoldSeconds = hold,
        Phase = phase, Text = compose(Speech(s, category, m, speed, target, hold, isEdgeCount: true)),
    };

    private int HoldSeconds(SessionState s)
    {
        bool punishment = s.Contract.Punishment;
        if (!r.Chance(punishment ? 60 : 35)) return 0;
        int min = punishment ? MinHoldPunishment : MinHoldNormal, max = punishment ? MaxHoldPunishment : MaxHoldNormal;
        return Math.Min(max, (int)((min + r.Next(0, max - min + 1)) * (1.0 + s.Mean / 40.0)));
    }

    /// <summary>The method used for stroking inside games and consequences.</summary>
    public MethodInfo Stroke(SessionState s) => a.Methods.Choose(s, new(Math.Min(10, s.Intensity + 1), Basic: true));

    public GameContext GameContext(SessionState s, MethodInfo stroke, DateTime? now = null)
        => new(s.Mean, s.Intensity, s.Arousal, s.Mood.Mood, stroke, a.Edges.DommeSets, s.EdgesThisStep, now ?? s.LastActivityUtc, r);

    public DommeVoice.SpeechContext Speech(SessionState s, StepCategory category, MethodInfo? method = null, int speed = 0,
        int durationOrCount = 0, int holdSeconds = 0, bool isEdgeCount = false) => new()
    {
        Mood = s.Mood.Mood, MoodIntensity = s.Mood.Intensity, Arousal = s.Arousal, Progress = s.Progress, StepNumber = s.StepNumber,
        EdgesThisSession = s.EdgesThisSession, EdgesThisStep = s.EdgesThisStep, IsRetaliating = s.Retaliation.Active,
        RetaliationReason = s.Retaliation.Reason, IsPunishment = s.Contract.Punishment, Method = method,
        PreviousCategory = s.PreviousCategory, Category = category, Speed = speed, Mean = s.Mean, DurationOrCount = durationOrCount,
        HoldSeconds = holdSeconds, LubeActive = s.LubeActive, SessionPoints = (int)s.Points, FalsePromiseActive = s.FalsePromiseActive,
        IsEdgeCount = isEdgeCount,
    };
}

/// <summary>Her mood: it lasts several steps, drifts with the session, and reacts to what the user does.</summary>
public static class Mood
{
    public static void Start(SessionState s, IRandom r)
    {
        s.Mood.Mood = s.Contract.Punishment
            ? r.Pick([DommeMood.Cruel, DommeMood.Relentless, DommeMood.Edgy])
            : r.Weighted<DommeMood>([(DommeMood.Neutral, 35), (DommeMood.Teasing, 15), (DommeMood.Edgy, 15), (DommeMood.Relentless, 13), (DommeMood.Cruel, 12), (DommeMood.Playful, 10)], DommeMood.Neutral);
        s.Mood.StepsRemaining = 4 + r.Next(0, 5);
        s.Mood.Intensity = 0.4 + r.NextDouble() * 0.4;
    }

    public static void Drift(SessionState s, IRandom r)
    {
        if (--s.Mood.StepsRemaining > 0) return;
        double progress = s.Progress;
        var weights = new Dictionary<DommeMood, double>
        {
            [DommeMood.Neutral] = 15, [DommeMood.Cruel] = 5 + s.Mean * 2, [DommeMood.Edgy] = 15 + (s.Arousal > 50 ? 15 : 0),
            [DommeMood.Teasing] = 15 + (progress < 0.3 ? 10 : 0), [DommeMood.Playful] = 8, [DommeMood.Relentless] = 10 + (progress > 0.5 ? 10 : 0),
        };
        weights[s.Mood.Mood] *= s.Mood.Mood is DommeMood.Neutral or DommeMood.Playful ? 0.3 : 0.4;
        if (s.Contract.Punishment)
        {
            weights[DommeMood.Teasing] *= 0.3; weights[DommeMood.Neutral] *= 0.3; weights[DommeMood.Playful] *= 0.2; weights[DommeMood.Cruel] *= 2.0;
        }
        s.Mood.Mood = r.Weighted(weights.Select(x => (x.Key, x.Value)).ToList(), DommeMood.Neutral);
        s.Mood.StepsRemaining = 4 + r.Next(0, 6);
        s.Mood.Intensity = 0.4 + r.NextDouble() * 0.5;
    }

    /// <summary>Positive delta: angrier. Negative: softer. Strong reactions can flip her mood.</summary>
    public static void Nudge(SessionState s, IRandom r, double delta)
    {
        s.Mood.Intensity = Math.Clamp(s.Mood.Intensity + delta * 0.15, 0.1, 1.0);
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
        if (moodDelta != 0) Nudge(s, r, moodDelta);
    }
}
