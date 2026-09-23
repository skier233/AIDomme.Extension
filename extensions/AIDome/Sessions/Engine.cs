using AIDome.Data;
using AIDome.Services;

namespace AIDome.Sessions;

/// <summary>Something the host must persist or act on after a transition.</summary>
public abstract record SessionEffect;
public sealed record GameStarted(GameState Game) : SessionEffect;
public sealed record GameFinished(GameState Game, GameResult? Result) : SessionEffect;
public sealed record PermissionAnswered(bool Granted, double Chance) : SessionEffect;
public sealed record SessionEnded(SessionOutcome Outcome) : SessionEffect;

/// <summary>What a transition produced: the domme's lines, system notices and effects.</summary>
public sealed class Transition
{
    public List<string> Domme { get; } = [];
    public List<string> Notices { get; } = [];
    public List<SessionEffect> Effects { get; } = [];
    /// <summary>The event did not apply (for example an answer with no game running).</summary>
    public string? Rejected { get; set; }
}

/// <summary>
/// The session engine. Each event mutates <see cref="SessionState"/> and returns a
/// <see cref="Transition"/>; it never touches the database, timers or the network, and all chance
/// comes from the injected <see cref="IRandom"/> and <see cref="IClock"/>. Every decision consults the
/// contract's authorities, never the mode name.
/// </summary>
public sealed class SessionEngine(IRandom random, IClock clock, Func<IRandom, Authorities, IPlanner>? planners = null)
{
    private readonly Func<IRandom, Authorities, IPlanner> planners = planners ?? ((r, a) => new RulePlanner(r, a));
    private IPlanner Planner(Authorities a) => planners(random, a);

    /// <summary>How often the domme checks in during free play (no countdown is shown).</summary>
    public const int FreePlayCheckSeconds = 60;

    public IRandom Random => random;
    public IClock Clock => clock;
    private DateTime Now => clock.UtcNow;

    public static Authorities AuthoritiesFor(SessionState s, IRandom random) => Authorities.For(s.Contract, random);

    /// <summary>Starts a prepared state (id, accepted contract, methods, history) and gives the first instruction.</summary>
    public Transition Begin(SessionState s)
    {
        var t = new Transition();
        s.StartedAtUtc = s.LastActivityUtc = s.StepStartedAtUtc = Now;
        s.Budget = Points.Budget(s.Length, s.Contract.Punishment);
        Mood.Start(s, random);
        if (s.Contract.Punishment) t.Domme.Add(DommeText.Pick(DommeText.PunishmentSessionIntro));
        Advance(s, t, completed: false);
        return t;
    }

    public Transition Edge(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || s.Paused) return t;
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        s.LastActivityUtc = Now;
        Accrue(s);
        s.Points += Points.ForEdge(s.ActiveMethod ?? s.Current?.Method ?? MethodInfo.UserChoice(s.Methods), s.EdgesThisSession);
        s.EdgesThisSession++;
        s.EdgesThisStep++;
        s.Arousal = Math.Min(95, s.Arousal + 15);

        if (s.Game is { } game)
        {
            if (Games.OnEdge(game, planner.GameContext(s, GameMethod(s, game), Now), s.EdgesThisStep) is { } step)
                HandleGame(s, t, planner, step);
            return t;
        }
        if (s.FinishDecided || s.Current is null) return t;

        if (!a.Edges.DommeSets)
        {
            // She is only watching the count; an occasional word keeps her present.
            if (random.Chance(35)) t.Domme.Add(EdgeRemark(s));
            if (!a.PlansSteps) CheckEnding(s, t, planner);
            return t;
        }

        var current = s.Current;
        if (current.Kind == InstructionKind.TimedMethod && current.Category != StepCategory.Finish)
        {
            int remaining = TimerRemaining(s);
            if (remaining > 5) { s.Suspended = current; s.SuspendedRemainingSeconds = remaining; }
            if (random.Chance(25 + s.Mean * 5))
            {
                int hold = s.Contract.Punishment ? random.Next(5, 15) : random.Next(3, 10);
                s.Holding = true;
                Present(s, t, planner.EdgeHold(s, hold), hold, newStep: false);
            }
            else
            {
                int cooldown = random.Next(3, 8);
                Present(s, t, planner.Cooldown(s, cooldown), cooldown, newStep: false);
            }
            return t;
        }
        if (current.Kind == InstructionKind.EdgeCount && s.EdgesThisStep >= current.DurationOrCount && !s.Holding)
        {
            if (current.HoldSeconds > 0)
            {
                s.Holding = true;
                Present(s, t, planner.EdgeHold(s, current.HoldSeconds), current.HoldSeconds, newStep: false);
            }
            else Advance(s, t, completed: true);
        }
        return t;
    }

    /// <summary>An edge recorded against an earlier step: it counts, but does not advance the current target.</summary>
    public Transition EarlierEdge(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return t;
        s.Points += Points.ForEdge(s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods), s.EdgesThisSession);
        s.EdgesThisSession++;
        t.Notices.Add("An edge from an earlier step was recorded. It counts toward the session, but not toward the current target.");
        return t;
    }

    /// <summary>A recorded orgasm. <paramref name="allowed"/> is whether permission stood when it was recorded.</summary>
    public Transition Orgasm(SessionState s, bool allowed)
    {
        var t = new Transition();
        if (!s.Active) return t;
        var planner = Planner(AuthoritiesFor(s, random));
        s.LastActivityUtc = Now;
        if (allowed)
        {
            t.Domme.Add(planner.EndLine(s, orgasmGranted: true));
            End(s, t, SessionOutcome.AllowedOrgasm);
            return t;
        }
        s.UnauthorizedOrgasms++;
        Mood.Retaliate(s, random, RetaliationReason.UnauthorizedOrgasm, 4, 8, 1.0);
        t.Domme.Add(DommeText.PickRetaliation(RetaliationReason.UnauthorizedOrgasm));
        if (s.Game is not null) AbandonGame(s, t);
        if (!s.FinishDecided) Advance(s, t, completed: false);
        return t;
    }

    public Transition Timer(SessionState s, long generation)
    {
        var t = new Transition();
        if (!s.Active || s.Paused || generation != s.TimerGeneration || s.TimerDueUtc is null || s.Current is null) return t;
        s.TimerDueUtc = null;
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        var current = s.Current;

        if (current.Category == StepCategory.Finish && s.PermissionGranted)
        {
            // The window closed without an orgasm.
            Accrue(s);
            s.PermissionGranted = false;
            Present(s, t, planner.Denied(s, "Time's up. You didn't cum in time. Denied."), 0, newStep: false);
            return t;
        }
        if (s.Game is { } game)
        {
            HandleGame(s, t, planner, Games.OnTimer(game, planner.GameContext(s, GameMethod(s, game), Now)));
            return t;
        }
        if (current.Category == StepCategory.FreePlay)
        {
            // Each quiet check-in is a "step" for her mood and for how long she stays annoyed.
            Accrue(s);
            Mood.Drift(s, random);
            if (s.Retaliation.Active && --s.Retaliation.StepsLeft <= 0) s.Retaliation.Active = false;
            if (!CheckEnding(s, t, planner)) SetTimer(s, FreePlayCheckSeconds);
            return t;
        }
        if (s.Holding)
        {
            s.Holding = false;
            s.Points += Points.ForHold(current.DurationOrCount);
        }
        if (s.Suspended is { } suspended)
        {
            int remaining = s.SuspendedRemainingSeconds;
            s.Suspended = null;
            Present(s, t, planner.Resume(s, suspended, remaining), remaining, newStep: false);
            return t;
        }
        Advance(s, t, completed: true);
        return t;
    }

    /// <summary>"Done": ready after applying lube, or acknowledging a denial.</summary>
    public Transition Complete(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || s.Current is null) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        if (s.Current.Kind == InstructionKind.Finish)
        {
            t.Domme.Add(Planner(AuthoritiesFor(s, random)).EndLine(s, orgasmGranted: false));
            End(s, t, s.UnauthorizedOrgasms > 0 ? SessionOutcome.UnauthorizedOrgasm : SessionOutcome.Denied);
            return t;
        }
        if (s.Current.Category == StepCategory.Finish && s.PermissionGranted)
            return Reject(t, "Record the orgasm when it happens. If the time runs out, you're denied.");
        if (s.PendingLube is not null && s.Current.Category == StepCategory.Rest && s.TimerDueUtc is null)
        {
            Advance(s, t, completed: false);
            return t;
        }
        return Reject(t, "This step advances through its timer, edge target or game.");
    }

    public Transition RequestPermission(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        if (s.FinishDecided) return Reject(t, "I've already decided how this ends.");
        if (s.Game is not null) return Reject(t, "Finish the game first.");
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        s.LastActivityUtc = Now;
        s.RequestsThisSession++;
        Accrue(s);
        double chance = a.Permission.Chance(s, requested: true);
        bool granted = random.Chance(chance);
        t.Effects.Add(new PermissionAnswered(granted, chance));
        if (granted)
        {
            s.FinishDecided = s.PermissionGranted = true;
            var finishing = planner.Finishing(s, requested: true);
            Present(s, t, finishing, finishing.DurationOrCount, newStep: true);
        }
        else
        {
            t.Domme.Add(DommeText.Pick(DommeText.BegCumDenied));
            Mood.Retaliate(s, random, RetaliationReason.BegCumDenied, 2, 5, 0.5);
            if (!a.PlansSteps) CheckEnding(s, t, planner);
        }
        return t;
    }

    public Transition RequestRest(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        var a = AuthoritiesFor(s, random);
        if (!a.Pacing.DommeSets) return Reject(t, "You set your own pace in this session. Rest whenever you like.");
        if (s.FinishDecided || s.Game is not null) return Reject(t, "Not now.");
        if (s.Current?.Kind == InstructionKind.Rest || s.Holding) { t.Domme.Add("You're already resting. Don't push it."); return t; }
        s.LastActivityUtc = Now;
        if (random.Next(0, 10) < (s.Arousal > 70 ? 1 : 3))
        {
            Mood.Nudge(s, random, -0.2);
            t.Domme.Add(DommeText.Pick(DommeText.BegStopGranted));
            var planner = Planner(a);
            Accrue(s);
            s.Suspended = null;
            s.Holding = false;
            var rest = new Directive
            {
                Kind = InstructionKind.Rest, Category = StepCategory.Rest, DurationOrCount = 15 + random.Next(0, 16), Phase = SessionPhase.Resting,
                Text = "Hands off. Catch your breath.",
            };
            s.PreviousCategory = StepCategory.Rest;
            s.StepsSinceRest = 0;
            Present(s, t, rest, rest.DurationOrCount, newStep: true);
        }
        else
        {
            t.Domme.Add(DommeText.Pick(DommeText.BegStopDenied));
            Mood.Retaliate(s, random, RetaliationReason.BegStopDenied, 2, 5, 0.7);
        }
        return t;
    }

    public Transition Answer(SessionState s, int answer)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        if (s.Game is not { } game) return Reject(t, "No game is waiting for an answer.");
        var planner = Planner(AuthoritiesFor(s, random));
        s.LastActivityUtc = Now;
        var step = Games.OnAnswer(game, answer, planner.GameContext(s, GameMethod(s, game), Now));
        if (step is null) return Reject(t, "That answer isn't available right now.");
        HandleGame(s, t, planner, step);
        return t;
    }

    /// <summary>An infraction the user reported themselves (disobedience, a ruined orgasm).</summary>
    public Transition Infraction(SessionState s, InfractionType type)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        var (reason, min, max, delta) = type switch
        {
            InfractionType.RuinedOrgasm => (RetaliationReason.RuinedOrgasm, 3, 6, 0.7),
            InfractionType.UnauthorizedOrgasm => (RetaliationReason.UnauthorizedOrgasm, 4, 8, 1.0),
            _ => (RetaliationReason.Disobedience, 3, 7, 0.8),
        };
        Mood.Retaliate(s, random, reason, min, max, delta);
        t.Domme.Add(DommeText.PickRetaliation(reason));
        if (s.Game is null && !s.FinishDecided && !s.Paused && AuthoritiesFor(s, random).PlansSteps) Advance(s, t, completed: true);
        return t;
    }

    public Transition Pause(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || s.Paused) return t;
        Accrue(s);
        s.ActiveSinceUtc = null;
        s.Paused = true;
        s.PausedAtUtc = Now;
        s.PausedTimerSeconds = TimerRemaining(s);
        s.TimerDueUtc = null;
        s.TimerGeneration++;
        t.Notices.Add("Session paused.");
        return t;
    }

    public Transition Resume(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || !s.Paused) return t;
        s.PausedSeconds += (Now - (s.PausedAtUtc ?? Now)).TotalSeconds;
        s.Paused = false;
        s.PausedAtUtc = null;
        s.LastActivityUtc = Now;
        if (Stimulating(s.Current)) s.ActiveSinceUtc = Now;
        if (s.PausedTimerSeconds > 0) SetTimer(s, Math.Max(1, s.PausedTimerSeconds));
        s.PausedTimerSeconds = 0;
        t.Notices.Add("Session resumed.");
        return t;
    }

    /// <summary>
    /// Replaces the contract mid-session (only when it allows changes). The running step or game is
    /// dropped without counting as a failure; recorded activity and points are kept.
    /// </summary>
    public Transition ChangeContract(SessionState s, SessionContract contract, List<MethodInfo> methods)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (!s.Contract.CanChangeDuringSession) throw new InvalidOperationException("Settings were locked at the start of this session.");
        if (contract.CanChangeDuringSession != s.Contract.CanChangeDuringSession)
            throw new InvalidOperationException("Whether settings may change was agreed before starting.");
        if (s.FinishDecided) throw new InvalidOperationException("The ending is already decided.");
        Accrue(s);
        if (s.Game is not null) AbandonGame(s, t);
        s.Contract = contract with { Preferences = s.Contract.Preferences, Punishment = s.Contract.Punishment, Debug = s.Contract.Debug };
        s.Methods = methods.Where(contract.Allows).ToList();
        s.ContractRevision++;
        s.PendingConsequences.Clear();
        s.PendingLube = null;
        s.Suspended = null;
        s.Holding = false;
        s.ChainStepsRemaining = 0;
        bool paused = s.Paused;
        s.Paused = false;
        Advance(s, t, completed: false);
        if (paused)
        {
            s.Paused = true;
            s.PausedAtUtc = Now;
            s.PausedTimerSeconds = TimerRemaining(s);
            s.TimerDueUtc = null;
            s.TimerGeneration++;
            s.ActiveSinceUtc = null;
        }
        t.Notices.Add("Session settings updated. The previous instruction was replaced without counting as a failure.");
        return t;
    }

    public Transition End(SessionState s, SessionOutcome outcome)
    {
        var t = new Transition();
        if (s.Active) End(s, t, outcome);
        return t;
    }

    // ── Internals ──

    private void End(SessionState s, Transition t, SessionOutcome outcome)
    {
        Accrue(s);
        if (s.Game is not null) AbandonGame(s, t);
        s.Ended = true;
        s.Outcome = outcome;
        s.Current = null;
        s.ActiveSinceUtc = null;
        s.TimerDueUtc = null;
        s.TimerGeneration++;
        t.Effects.Add(new SessionEnded(outcome));
    }

    /// <summary>Completes the current step (if any) and presents the next one, or her ending decision.</summary>
    private void Advance(SessionState s, Transition t, bool completed)
    {
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        Accrue(s);
        if (completed && s.Current is { } done) UpdateArousal(s, done);
        s.Holding = false;
        s.Suspended = null;
        // Once she has decided the ending, nothing else is planned.
        if (s.FinishDecided) return;

        if (!a.PlansSteps)
        {
            if (!CheckEnding(s, t, planner))
                Present(s, t, planner.FreePlay(s, FreePlayCheckSeconds), FreePlayCheckSeconds, newStep: true);
            return;
        }
        if (s.PendingConsequences.Count == 0 && s.PendingLube is null && CheckEnding(s, t, planner)) return;

        var next = planner.Next(s);
        if (next.Game is GameType type)
        {
            var stroke = planner.Stroke(s);
            s.EdgesThisStep = 0;
            var (game, first) = Games.Start(type, planner.GameContext(s, stroke, Now));
            s.Game = game;
            s.RecentGames.Add(type);
            if (s.RecentGames.Count > 5) s.RecentGames.RemoveAt(0);
            t.Effects.Add(new GameStarted(game));
            Present(s, t, first.Next!, first.TimerSeconds ?? 0, newStep: true);
            return;
        }
        Present(s, t, next.Directive!, next.TimerSeconds, newStep: true);
    }

    /// <summary>When the points reach the budget she decides the ending herself, in every mode.</summary>
    private bool CheckEnding(SessionState s, Transition t, IPlanner planner)
    {
        if (s.FinishDecided || s.Contract.GamesOnly || s.Game is not null || s.Retaliation.Active || s.Points < s.Budget) return false;
        var a = AuthoritiesFor(s, random);
        double chance = a.Permission.Chance(s, requested: false);
        bool granted = random.Chance(chance);
        s.FinishDecided = true;
        if (granted)
        {
            s.PermissionGranted = true;
            var finishing = planner.Finishing(s, requested: false);
            Present(s, t, finishing, finishing.DurationOrCount, newStep: true);
        }
        else Present(s, t, planner.Denied(s), 0, newStep: true);
        return true;
    }

    private void HandleGame(SessionState s, Transition t, IPlanner planner, GameStep step)
    {
        if (step.Result is { } result)
        {
            var game = s.Game!;
            s.Game = null;
            t.Effects.Add(new GameFinished(game, result));
            t.Domme.Add(result.Line);
            if (!result.Neutral)
            {
                if (result.Won) Mood.Nudge(s, random, -0.4);
                else Mood.Retaliate(s, random, RetaliationReason.LostGame, 2, 5, 0.6);
            }
            foreach (var consequence in result.Consequences)
            {
                switch (consequence.Kind)
                {
                    case ConsequenceKind.Points: s.Points = Math.Max(0, s.Points + consequence.Amount); break;
                    case ConsequenceKind.PermissionOdds: s.PermissionModifier += consequence.Amount; break;
                    default: s.PendingConsequences.Add(consequence); break;
                }
            }
            Advance(s, t, completed: true);
            return;
        }
        var next = step.Next!;
        if (step.TimerSeconds is int seconds) Present(s, t, next, seconds, newStep: false);
        else
        {
            // Same timer keeps running; only the instruction changes.
            int remaining = TimerRemaining(s);
            Present(s, t, next with { DurationOrCount = remaining }, remaining, newStep: false, keepTimer: true);
        }
    }

    private void AbandonGame(SessionState s, Transition t)
    {
        // Leaving a game early (mode change, ending) never fabricates a loss.
        t.Effects.Add(new GameFinished(s.Game!, null));
        s.Game = null;
    }

    /// <summary>Makes a directive current: metronome tempo, points bookkeeping, timer, and her words.</summary>
    private void Present(SessionState s, Transition t, Directive d, int timerSeconds, bool newStep, bool keepTimer = false)
    {
        var a = AuthoritiesFor(s, random);
        bool stimulating = Stimulating(d);
        d = d with
        {
            Tempo = stimulating && d.Category != StepCategory.Hold ? a.Pacing.Tempo(d.Speed) : 0,
            DurationOrCount = d.Kind == InstructionKind.FreeText && d.DurationOrCount == 0 && timerSeconds > 0 ? timerSeconds : d.DurationOrCount,
        };
        Accrue(s);
        if (newStep)
        {
            s.StepNumber++;
            s.EdgesThisStep = 0;
        }
        s.Current = d;
        s.StepStartedAtUtc = Now;
        s.ActiveSinceUtc = stimulating && !s.Paused ? Now : null;
        s.ActiveMethod = stimulating ? d.Method : s.ActiveMethod;
        s.ActiveSpeed = stimulating ? d.Speed : 0;
        if (!keepTimer) SetTimer(s, timerSeconds);
        if (!d.HiddenTimer || newStep) t.Domme.Add(d.Text);
    }

    private static MethodInfo GameMethod(SessionState s, GameState game) => game.Method ?? MethodInfo.UserChoice(s.Methods);

    private static bool Stimulating(Directive? d)
        => d is { Method: not null } && d.Category is not (StepCategory.Rest or StepCategory.Hold) && d.Kind != InstructionKind.Finish;

    /// <summary>Converts stimulation since the last accrual into points (free play stops counting when unreported).</summary>
    private void Accrue(SessionState s)
    {
        if (s.ActiveSinceUtc is not DateTime since || s.Paused) return;
        var end = Now;
        if (!AuthoritiesFor(s, random).Pacing.DommeSets)
        {
            var cutoff = s.LastActivityUtc + Points.MaxUnreportedStimulation;
            if (cutoff < end) end = cutoff;
        }
        if (end > since)
            s.Points += Points.ForStimulation(s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods), (end - since).TotalSeconds, s.ActiveSpeed);
        s.ActiveSinceUtc = Now;
    }

    private static void UpdateArousal(SessionState s, Directive d)
    {
        switch (d.Kind)
        {
            case InstructionKind.TimedMethod:
                double speedFactor = (d.Speed > 0 ? d.Speed : 5) / 10.0;
                s.Arousal = Math.Min(95, s.Arousal + (d.Method?.Closeness ?? 3) * speedFactor * Math.Min(d.DurationOrCount / 60.0, 2.5) * 5.0);
                break;
            case InstructionKind.Rest:
                s.Arousal = Math.Max(0, s.Arousal - d.DurationOrCount * 0.4);
                break;
            case InstructionKind.FreeText:
                s.Arousal = Math.Max(0, s.Arousal - 5);
                break;
        }
    }

    private void SetTimer(SessionState s, int seconds)
    {
        s.TimerGeneration++;
        s.TimerDueUtc = seconds > 0 ? Now.AddSeconds(seconds) : null;
    }

    private int TimerRemaining(SessionState s)
        => s.TimerDueUtc is DateTime due ? Math.Max(0, (int)Math.Ceiling((due - Now).TotalSeconds)) : 0;

    private string EdgeRemark(SessionState s)
    {
        string count = s.EdgesThisSession == 1 ? "That's one." : $"That's {s.EdgesThisSession}.";
        return s.Mood.Mood switch
        {
            DommeMood.Cruel => random.Pick([$"{count} Keep going.", $"{count} Don't you dare go over."]),
            DommeMood.Teasing => random.Pick([$"Mmm. {count}", $"{count} I love watching you suffer."]),
            DommeMood.Playful => random.Pick([$"Ooh! {count}", $"Nice one. {count}"]),
            _ => random.Pick([$"Good. {count}", count]),
        };
    }

    private static Transition Reject(Transition t, string reason)
    {
        t.Rejected = reason;
        return t;
    }
}
