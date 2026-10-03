using AIDome.Data;
using AIDome.Sessions.Plan;
using AIDome.Speech;

namespace AIDome.Sessions;

/// <summary>Something the host must persist or act on after a transition.</summary>
public abstract record SessionEffect;
public sealed record GameStarted(GameState Game) : SessionEffect;
public sealed record GameFinished(GameState Game, GameResult? Result) : SessionEffect;
public sealed record PermissionAnswered(bool Granted, double Chance) : SessionEffect;
/// <summary>The session is over; the state says what it served, for settling the ledger.</summary>
public sealed record SessionEnded(SessionOutcome Outcome, SessionState? State = null) : SessionEffect;
public sealed record OfferMade(PendingOffer Offer) : SessionEffect;
public sealed record OfferDecided(PendingOffer Offer, OfferStatus Status) : SessionEffect;
/// <summary>A new ledger entry: a price accepted during the session.</summary>
public sealed record LedgerOwed(Stake Stake, LedgerOrigin Origin, string Reason, Guid? OfferId = null) : SessionEffect;

/// <summary>Why an orgasm is off the table.</summary>
public enum Block { Promise, Denial, Sold, OwedEdges }

/// <summary>What a transition produced: what she should say, system notices and effects.</summary>
public sealed class Transition
{
    /// <summary>What she says, as utterances; the narrator gives them words.</summary>
    public List<Utterance> Say { get; } = [];
    public List<string> Notices { get; } = [];
    public List<SessionEffect> Effects { get; } = [];
    /// <summary>The event did not apply (for example an answer with no game running).</summary>
    public string? Rejected { get; set; }
}

/// <summary>
/// The session engine. Each event mutates <see cref="SessionState"/> and returns a
/// <see cref="Transition"/>; it never touches the database, timers, the network or words, and all
/// chance comes from the injected <see cref="IRandom"/> and <see cref="IClock"/>. Guided sessions run
/// her plan (<see cref="PlanEngine"/>) and events revise it; every decision consults the contract's
/// authorities, never the mode name.
/// </summary>
public sealed class SessionEngine(IRandom random, IClock clock, Func<IRandom, Authorities, IPlanner>? planners = null)
{
    private readonly Func<IRandom, Authorities, IPlanner> planners = planners ?? ((r, a) => new SegmentPlanner(r, a));
    private IPlanner Planner(Authorities a) => planners(random, a);

    /// <summary>How often the domme checks in during free play (no countdown is shown).</summary>
    public const int FreePlayCheckSeconds = 60;

    public IRandom Random => random;
    public IClock Clock => clock;
    private DateTime Now => clock.UtcNow;

    public static Authorities AuthoritiesFor(SessionState s, IRandom random) => Authorities.For(s.Contract, random);

    /// <summary>
    /// Starts a prepared state (id, accepted contract, methods, recall, preferences in an empty plan)
    /// and gives the first instruction. Guided sessions get their plan here; every session gets the
    /// ending she leans toward.
    /// </summary>
    public Transition Begin(SessionState s)
    {
        var t = new Transition();
        s.StartedAtUtc = s.LastActivityUtc = s.StepStartedAtUtc = Now;
        s.Body.AsOfUtc = Now;
        s.Budget = Points.Budget(s.Length, s.Contract.Punishment, s.Lengths, s.Recall);
        var owed = s.Contract.Obligations;
        s.DenialUntilUtc = owed.NoOrgasmUntilUtc;
        s.DenialReason = owed.NoOrgasmReason;
        s.OwedEdges = owed.OwedEdges;
        s.OwedRuins = owed.OwedRuins;
        s.OwedEndings = owed.PunishmentEnding ? 1 : 0;
        Mood.Start(s, random);
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        s.Plan = a.PlansSteps ? planner.Plans.Make(s, a) : null;
        s.Ending = EndingAuthority.Lean(s, random);

        var start = Utterance.Of(SpeechAct.SessionStart, $"mode:{s.Contract.Mode}", $"mood:{s.Mood.Mood}", $"cause:{s.Mood.Cause}");
        if (s.Contract.Punishment) start = start.Tagged(s.Contract.Obligations.PunishmentSession ? "owed-punishment" : "punishment");
        t.Say.Add(start);
        if (ObligationLine(s) is { } obligations) t.Say.Add(obligations);
        Advance(s, t, completed: false);
        return t;
    }

    public Transition Edge(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || s.Paused) return t;
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        ExpireOffer(s, t);
        s.LastActivityUtc = Now;
        Accrue(s);
        { double edgePoints = Points.ForEdge(s.ActiveMethod ?? s.Current?.Method ?? MethodInfo.UserChoice(s.Methods), s.EdgesThisSession); s.Points += edgePoints; s.Earned.Edges += edgePoints; }
        s.EdgesThisSession++;
        s.EdgesThisStep++;
        // An edge is 100% by definition; where she chose the method and pace it also teaches her his body.
        // Only the first edge of a step does: after it, how long he waits before the next is his own business.
        int checks = s.Body.Checks.Count;
        Body.OnEdge(s.Body, s.Profile, learn: a.Pacing.DommeSets && s.ActiveMethod is { IsUserChoice: false } && s.Game is null && s.EdgesThisStep == 1,
            (Now - s.StartedAtUtc).TotalSeconds, estimate: true);
        // He got there much faster or slower than she expected: she notices, now and then.
        if (s.Body.Checks.Count > checks && s.Body.Checks[^1] is { ExpectedSeconds: >= 10 and < Body.Unreachable } check && s.Game is null && s.Price is null && random.Chance(60))
        {
            double ratio = check.ActualSeconds / check.ExpectedSeconds;
            string? pace = ratio <= 0.65 ? (check.RestBefore >= 5 ? "quick-back" : "quick") : ratio >= 1.6 ? "slow" : null;
            if (pace is not null) t.Say.Add(Utterance.Of(SpeechAct.EdgeRemark, "pace", pace, $"mood:{s.Mood.Mood}").With("edges", s.EdgesThisSession));
        }
        PayOwedEdge(s, t);

        if (s.Game is { } game)
        {
            if (Games.OnEdge(game, planner.GameContext(s, GameMethod(s, game), Now), s.EdgesThisStep) is { } step)
                HandleGame(s, t, planner, step);
            return t;
        }
        if (s.Price is { } price)
        {
            if (s.Holding) return t;
            price.EdgesLeft--;
            if (price.EdgesLeft > 0)
            {
                if (s.Current?.Kind != InstructionKind.EdgeCount) t.Say.Add(Utterance.Of(SpeechAct.PriceProgress).With("left", price.EdgesLeft));
                return t;
            }
            if (price.Hold > 0)
            {
                s.Holding = true;
                Present(s, t, planner.EdgeHold(s, price.Hold), price.Hold, newStep: false, act: SpeechAct.Hold);
            }
            else PayPrice(s, t, planner);
            return t;
        }
        if (s.FinishDecided || s.Current is null) return t;

        var current = s.Current;
        // Challenges: an edge fails "don't you dare", and counts toward "as many as you can".
        if (current.ForbidEdge)
        {
            s.ChallengesFailed++;
            Mood.Retaliate(s, random, RetaliationReason.FailedChallenge, 2, 4, 0.5);
            t.Say.Add(Utterance.Of(SpeechAct.ChallengeFailed, $"segment:{current.Segment}").With("seconds", (int)(Now - s.StepStartedAtUtc).TotalSeconds));
            if (s.Plan is not null) planner.Plans.OnChallengeFailed(s, a, Now);
            if (a.Edges.DommeSets)
            {
                int cooldown = random.Next(5, 10);
                Present(s, t, planner.Cooldown(s, cooldown), cooldown, newStep: true, act: SpeechAct.Cooldown, tags: "failed");
            }
            else Advance(s, t, completed: true);
            return t;
        }
        if (current.CountEdges)
        {
            t.Say.Add(Utterance.Of(SpeechAct.EdgeCounted).With("made", s.EdgesThisStep));
            return t;
        }

        if (!a.Edges.DommeSets)
        {
            // She is only watching the count; an occasional word keeps her present.
            t.Say.Add(Utterance.Of(SpeechAct.EdgeRemark, $"mood:{s.Mood.Mood}").With("edges", s.EdgesThisSession));
            if (!a.PlansSteps) CheckEnding(s, t, planner);
            return t;
        }

        if (current.Kind == InstructionKind.TimedMethod && current.Category != StepCategory.Finish)
        {
            int remaining = TimerRemaining(s);
            if (remaining > 5) { s.Suspended = current; s.SuspendedRemainingSeconds = remaining; }
            if (random.Chance(25 + s.Mean * 5))
            {
                int hold = s.Contract.Punishment ? random.Next(5, 15) : random.Next(3, 10);
                s.Holding = true;
                Present(s, t, planner.EdgeHold(s, hold), hold, newStep: false, act: SpeechAct.Hold);
            }
            else
            {
                int cooldown = random.Next(3, 8);
                Present(s, t, planner.Cooldown(s, cooldown), cooldown, newStep: false, act: SpeechAct.Cooldown);
            }
            return t;
        }
        if (current.Kind == InstructionKind.EdgeCount && s.EdgesThisStep >= current.DurationOrCount && !s.Holding)
        {
            if (current.HoldSeconds > 0)
            {
                s.Holding = true;
                Present(s, t, planner.EdgeHold(s, current.HoldSeconds), current.HoldSeconds, newStep: false, act: SpeechAct.Hold);
            }
            else Advance(s, t, completed: true);
        }
        else if (current.Kind == InstructionKind.EdgeCount && !s.Holding)
            t.Say.Add(Utterance.Of(SpeechAct.EdgeRemark, "target").With("left", current.DurationOrCount - s.EdgesThisStep).With("edges", s.EdgesThisSession));
        return t;
    }

    /// <summary>An edge recorded against an earlier step: it counts, but does not advance the current target.</summary>
    public Transition EarlierEdge(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return t;
        { double edgePoints = Points.ForEdge(s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods), s.EdgesThisSession); s.Points += edgePoints; s.Earned.Edges += edgePoints; }
        s.EdgesThisSession++;
        PayOwedEdge(s, t);
        t.Notices.Add("An edge from an earlier step was recorded. It counts toward the session, but not toward the current target.");
        return t;
    }

    /// <summary>A recorded orgasm. <paramref name="allowed"/> is whether permission stood when it was recorded.</summary>
    public Transition Orgasm(SessionState s, bool allowed)
    {
        var t = new Transition();
        if (!s.Active) return t;
        var a = AuthoritiesFor(s, random);
        s.LastActivityUtc = Now;
        Feel(s);
        if (!allowed) Body.OnSlip(s.Body, s.Profile);
        Body.OnOrgasm(s.Body);
        if (allowed)
        {
            string how = s.FinishEnderId is not null ? "punishment-ending" : s.FinishRuined ? "ruined" : "full";
            ServeFinish(s);
            t.Say.Add(Utterance.Of(SpeechAct.SessionEnd, "granted", $"ending:{how}", $"mood:{s.Mood.Mood}"));
            End(s, t, SessionOutcome.AllowedOrgasm);
            return t;
        }
        s.UnauthorizedOrgasms++;
        Mood.Retaliate(s, random, RetaliationReason.UnauthorizedOrgasm, 4, 8, 1.0);
        t.Say.Add(Utterance.Of(SpeechAct.Retaliation, $"reason:{RetaliationReason.UnauthorizedOrgasm}"));
        if (s.Game is not null) AbandonGame(s, t);
        if (s.Offer is not null) Decide(s, t, OfferStatus.Expired);
        s.Price = null;
        if (s.Plan is not null && !s.FinishDecided) Planner(a).Plans.OnUnauthorizedOrgasm(s, a, Now);
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
        Feel(s);
        ExpireOffer(s, t);

        if (current.Category == StepCategory.Finish && s.PermissionGranted)
        {
            // The window closed without an orgasm.
            Accrue(s);
            s.PermissionGranted = false;
            t.Say.Add(Utterance.Of(SpeechAct.TimeUp));
            Present(s, t, planner.Denied(s), 0, newStep: false, act: SpeechAct.Denied, tags: "too-slow");
            return t;
        }
        if (current.Category == StepCategory.Offer)
        {
            // She waited long enough: the punishment stands.
            if (s.Offer is { } offer)
            {
                t.Say.Add(Utterance.Of(SpeechAct.OfferExpired, "mercy"));
                QueueStakes(s, offer.Replaces);
                Decide(s, t, OfferStatus.Expired);
            }
            Advance(s, t, completed: false);
            return t;
        }
        if (s.Game is { } game)
        {
            HandleGame(s, t, planner, Games.OnTimer(game, planner.GameContext(s, GameMethod(s, game), Now)));
            return t;
        }
        if (s.Holding && s.Price is not null)
        {
            s.Holding = false;
            { double holdPoints = Points.ForHold(current.DurationOrCount); s.Points += holdPoints; s.Earned.Holds += holdPoints; }
            PayPrice(s, t, planner);
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
            { double holdPoints = Points.ForHold(current.DurationOrCount); s.Points += holdPoints; s.Earned.Holds += holdPoints; }
        }
        if (s.Suspended is { } suspended)
        {
            int remaining = s.SuspendedRemainingSeconds;
            s.Suspended = null;
            Present(s, t, planner.Resume(s, suspended, remaining), remaining, newStep: false, act: SpeechAct.Resume);
            return t;
        }
        if (current.ForbidEdge)
        {
            s.ChallengesPassed++;
            Mood.Nudge(s, random, -0.3, MoodCause.Obedience);
            t.Say.Add(Utterance.Of(SpeechAct.ChallengeResult, "passed", $"segment:{current.Segment}"));
        }
        else if (current.CountEdges)
        {
            // What she expected: roughly one edge every half minute at her tempo.
            int expected = Math.Max(1, current.DurationOrCount / 30);
            string how = s.EdgesThisStep >= expected + 1 ? "impressed" : s.EdgesThisStep >= expected ? "passed" : "poor";
            if (how == "poor") Mood.Nudge(s, random, 0.2, MoodCause.FailedChallenge);
            else Mood.Nudge(s, random, -0.2, MoodCause.Obedience);
            t.Say.Add(Utterance.Of(SpeechAct.ChallengeResult, how, $"segment:{current.Segment}").With("made", s.EdgesThisStep).With("target", expected));
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
            bool ender = s.FinishEnderId is not null;
            ServeFinish(s);
            t.Say.Add(Utterance.Of(SpeechAct.SessionEnd, "denied", ender ? "ending:punishment-ending" : "ending:denied", $"mood:{s.Mood.Mood}"));
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
        ExpireOffer(s, t);
        if (s.Offer is not null) return Reject(t, "Answer her offer first.");
        if (s.Price is not null) return Reject(t, "Pay what you agreed to first.");
        if (s.FinishDecided) return Reject(t, "I've already decided how this ends.");
        if (s.Game is not null) return Reject(t, "Finish the game first.");
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        s.LastActivityUtc = Now;
        s.RequestsThisSession++;
        Accrue(s);
        if (Blocked(s) is { } block)
        {
            // Promises and debts are not up for negotiation.
            t.Effects.Add(new PermissionAnswered(false, 0));
            t.Say.Add(BlockedLine(s, block));
            s.RequestsDenied++;
            Mood.Nudge(s, random, 0.2, MoodCause.Begging);
            return t;
        }
        double chance = a.Permission.Chance(s, requested: true);
        bool granted = random.Chance(chance);
        t.Effects.Add(new PermissionAnswered(granted, chance));
        if (granted)
        {
            Grant(s, t, planner, requested: true);
            return t;
        }
        s.RequestsDenied++;
        if (MayOffer(s, OfferBuys.Orgasm) && Offers.Make(s, OfferBuys.Orgasm, random, Now) is { } offer)
        {
            t.Say.Add(OfferLine(offer));
            Put(s, t, offer);
            return t;
        }
        t.Say.Add(Utterance.Of(SpeechAct.BegCumDenied, $"mood:{s.Mood.Mood}").With("begs", s.RequestsThisSession));
        Mood.Retaliate(s, random, RetaliationReason.BegCumDenied, 2, 5, 0.5);
        if (s.Plan is not null) planner.Plans.OnPestering(s, a, Now);
        if (!a.PlansSteps) CheckEnding(s, t, planner);
        return t;
    }

    public Transition RequestRest(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        var a = AuthoritiesFor(s, random);
        if (!a.Pacing.DommeSets) return Reject(t, "You set your own pace in this session. Rest whenever you like.");
        ExpireOffer(s, t);
        if (s.Offer is not null) return Reject(t, "Answer her offer first.");
        if (s.FinishDecided || s.Game is not null || s.Price is not null) return Reject(t, "Not now.");
        if (s.Current?.Kind == InstructionKind.Rest || s.Holding) { t.Say.Add(Utterance.Of(SpeechAct.AlreadyResting)); return t; }
        s.LastActivityUtc = Now;
        s.RestRequests++;
        var planner = Planner(a);
        // The segment she is running may say what she does with a rest beg; otherwise the usual odds.
        var outcome = s.Plan is not null ? planner.Plans.OnRestBeg(s, a, Now) : RestBegOutcome.NoRule;
        bool granted = outcome == RestBegOutcome.Granted || outcome == RestBegOutcome.NoRule && random.Next(0, 10) < (s.Arousal > 70 ? 1 : 3);
        if (granted)
        {
            Mood.Nudge(s, random, -0.2);
            t.Say.Add(Utterance.Of(SpeechAct.BegRestGranted, outcome == RestBegOutcome.Granted ? "twist" : "plain"));
            Rest(s, t);
            return t;
        }
        if (MayOffer(s, OfferBuys.Rest) && Offers.Make(s, OfferBuys.Rest, random, Now) is { } offer)
        {
            t.Say.Add(OfferLine(offer));
            Put(s, t, offer);
            return t;
        }
        t.Say.Add(Utterance.Of(SpeechAct.BegRestDenied, $"mood:{s.Mood.Mood}").With("begs", s.RestRequests));
        Mood.Retaliate(s, random, RetaliationReason.BegStopDenied, 2, 5, 0.7);
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

    /// <summary>The user takes her deal.</summary>
    public Transition AcceptOffer(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        ExpireOffer(s, t);
        if (s.Offer is not { } offer) return Reject(t, t.Effects.Count > 0 ? "Too late. That offer's gone." : "There's no offer on the table.");
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        s.LastActivityUtc = Now;
        s.OffersAccepted++;
        Decide(s, t, OfferStatus.Accepted);
        Mood.Nudge(s, random, -0.2, MoodCause.Deal);
        if (offer.Kind == OfferKind.EdgesNow)
        {
            s.Price = new() { OfferId = offer.Id, EdgesLeft = Math.Max(1, offer.Amount), Hold = offer.Hold, Reward = offer.Buys };
            // A deal struck mid-hold ends the hold: paying starts from here.
            s.Holding = false;
            t.Say.Add(Utterance.Of(SpeechAct.OfferAccepted, "pay-now", $"buys:{offer.Buys}"));
            var step = planner.PriceStep(s, s.Price);
            Present(s, t, step, step.HiddenTimer ? FreePlayCheckSeconds : 0, newStep: true, act: SpeechAct.Price);
            return t;
        }
        if (offer.Stake is { } stake)
        {
            t.Effects.Add(new LedgerOwed(stake, LedgerOrigin.Offer, $"The price of {Bought(offer.Buys)}: {offer.Text}", offer.Id));
            // A price for an orgasm starts after it; a price for a rest or mercy applies from now.
            if (offer.Buys != OfferBuys.Orgasm)
            {
                switch (stake.Kind)
                {
                    case LedgerKind.Edges: s.OwedEdges += stake.Amount; break;
                    case LedgerKind.Ruin: s.OwedRuins += stake.Amount; break;
                    case LedgerKind.Denial:
                        var until = Now.AddDays(stake.Amount);
                        if (s.DenialUntilUtc is not DateTime current || current < until)
                        {
                            s.DenialUntilUtc = until;
                            s.DenialReason = nameof(Block.Sold);
                        }
                        break;
                }
            }
        }
        t.Say.Add(Utterance.Of(SpeechAct.OfferAccepted, "pay-later", $"buys:{offer.Buys}"));
        GrantReward(s, t, planner, offer.Buys, ruinNow: offer.Kind == OfferKind.RuinNow);
        return t;
    }

    public Transition DeclineOffer(SessionState s)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (s.Paused) return Reject(t, "Resume the session first.");
        ExpireOffer(s, t);
        if (s.Offer is not { } offer) return Reject(t, t.Effects.Count > 0 ? "Too late. That offer's gone." : "There's no offer on the table.");
        s.LastActivityUtc = Now;
        s.OffersDeclined++;
        Decide(s, t, OfferStatus.Declined);
        Mood.Nudge(s, random, 0.3);
        t.Say.Add(Utterance.Of(SpeechAct.OfferDeclined, $"buys:{offer.Buys}"));
        if (offer.Buys == OfferBuys.Mercy)
        {
            QueueStakes(s, offer.Replaces);
            Advance(s, t, completed: false);
        }
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
        t.Say.Add(Utterance.Of(SpeechAct.Retaliation, $"reason:{reason}", "confessed"));
        if (s.Game is null && s.Price is null && s.Offer is null && !s.FinishDecided && !s.Paused && AuthoritiesFor(s, random).PlansSteps)
            Advance(s, t, completed: true);
        return t;
    }

    public Transition Pause(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || s.Paused) return t;
        Freeze(s, Now);
        t.Notices.Add("Session paused.");
        return t;
    }

    /// <summary>
    /// Nobody is there (Cove closed, or no page has checked in): the session stops as of
    /// <paramref name="at"/>, and she will ask what happened before it can go on.
    /// </summary>
    public Transition Interrupt(SessionState s, DateTime at)
    {
        var t = new Transition();
        if (!s.Active || s.InterruptedAtUtc is not null) return t;
        if (at > Now) at = Now;
        if (!s.Paused) Freeze(s, at < s.StepStartedAtUtc ? Now : at);
        s.InterruptedAtUtc = at;
        t.Notices.Add("The session stopped: nobody was here. She'll want to know what happened.");
        return t;
    }

    /// <summary>The user explained the interruption; the session may be resumed.</summary>
    public void ClearInterruption(SessionState s) => s.InterruptedAtUtc = null;

    public Transition Resume(SessionState s)
    {
        var t = new Transition();
        if (!s.Active || !s.Paused) return t;
        if (s.InterruptedAtUtc is not null) return Reject(t, "Tell her what happened first.");
        Feel(s);
        s.PausedSeconds += (Now - (s.PausedAtUtc ?? Now)).TotalSeconds;
        s.Paused = false;
        s.PausedAtUtc = null;
        s.LastActivityUtc = Now;
        if (Stimulating(s.Current)) s.ActiveSinceUtc = Now;
        if (s.Offer is not null) s.Offer.ExpiresAtUtc = Now.AddSeconds(Offers.ExpirySeconds);
        if (s.PausedTimerSeconds > 0) SetTimer(s, Math.Max(1, s.PausedTimerSeconds));
        s.PausedTimerSeconds = 0;
        // Her chatter clock restarts with the session.
        s.Speech.ChatterDueUtc = null;
        s.Speech.LastCommandUtc = Now;
        t.Notices.Add("Session resumed.");
        return t;
    }

    /// <summary>
    /// Replaces the contract mid-session (only when it allows changes). The running step or game is
    /// dropped without counting as a failure; recorded activity and points are kept, and the rest of
    /// the plan is made again.
    /// </summary>
    public Transition ChangeContract(SessionState s, SessionContract contract, List<MethodInfo> methods)
    {
        var t = new Transition();
        if (!s.Active) return Reject(t, "No active session.");
        if (!s.Contract.CanChangeDuringSession) throw new InvalidOperationException("Settings were locked at the start of this session.");
        if (contract.CanChangeDuringSession != s.Contract.CanChangeDuringSession)
            throw new InvalidOperationException("Whether settings may change was agreed before starting.");
        if (s.FinishDecided) throw new InvalidOperationException("The ending is already decided.");
        if (s.Offer is not null || s.Price is not null) throw new InvalidOperationException("Settle her deal before changing settings.");
        Accrue(s);
        if (s.Game is not null) AbandonGame(s, t);
        s.Contract = contract with
        {
            Preferences = s.Contract.Preferences, Punishment = s.Contract.Punishment, Debug = s.Contract.Debug, Obligations = s.Contract.Obligations,
            Persona = s.Contract.Persona,
        };
        s.Methods = methods.Where(contract.Allows).ToList();
        s.ContractRevision++;
        s.PendingConsequences.Clear();
        s.PendingLube = null;
        s.Suspended = null;
        s.Holding = false;
        s.ChainStepsRemaining = 0;
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        if (!a.PlansSteps) s.Plan = null;
        else if (s.Plan is null) s.Plan = planner.Plans.Make(s, a);
        else planner.Plans.OnContractChanged(s, a, Now);
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

    /// <summary>Whether an orgasm is off the table right now, and why.</summary>
    public Block? Blocked(SessionState s)
    {
        if (s.DenialUntilUtc is DateTime until && Now < until)
            return Enum.TryParse<Block>(s.DenialReason, true, out var why) ? why : Block.Denial;
        if (s.OwedEdges > 0) return Block.OwedEdges;
        return null;
    }

    private Utterance BlockedLine(SessionState s, Block block)
    {
        var u = Utterance.Of(SpeechAct.Blocked, $"why:{block}", $"mood:{s.Mood.Mood}").With("begs", s.RequestsThisSession);
        if (block == Block.OwedEdges) return u.With("owed", Stake.Plural(s.OwedEdges, "edge"));
        if (s.DenialUntilUtc is DateTime until)
            u = until == DateTime.MaxValue ? u.Tagged("this-session") : u.With("left", Left(until - Now));
        return u;
    }

    private static string Left(TimeSpan left)
        => left.TotalHours < 24 ? Stake.Plural(Math.Max(1, (int)Math.Ceiling(left.TotalHours)), "more hour") : Stake.Plural((int)Math.Ceiling(left.TotalDays), "more day");

    // ── Internals ──

    private void End(SessionState s, Transition t, SessionOutcome outcome)
    {
        Accrue(s);
        if (s.Game is not null) AbandonGame(s, t);
        if (s.Offer is not null) Decide(s, t, OfferStatus.Expired);
        s.Price = null;
        s.Ended = true;
        s.Outcome = outcome;
        s.Current = null;
        s.ActiveSinceUtc = null;
        s.TimerDueUtc = null;
        s.TimerGeneration++;
        t.Effects.Add(new SessionEnded(outcome, s));
    }

    /// <summary>Stops the countdown and stimulation as of <paramref name="at"/>.</summary>
    private void Freeze(SessionState s, DateTime at)
    {
        Accrue(s, at);
        s.ActiveSinceUtc = null;
        s.Paused = true;
        s.PausedAtUtc = at;
        s.PausedTimerSeconds = s.TimerDueUtc is DateTime due ? Math.Max(0, (int)Math.Ceiling((due - at).TotalSeconds)) : 0;
        s.TimerDueUtc = null;
        s.TimerGeneration++;
    }

    /// <summary>Completes the current step (if any) and presents the next one from the plan, or her ending.</summary>
    private void Advance(SessionState s, Transition t, bool completed)
    {
        var a = AuthoritiesFor(s, random);
        var planner = Planner(a);
        Accrue(s);
        s.Holding = false;
        s.Suspended = null;
        // Once she has decided the ending, nothing else is planned.
        if (s.FinishDecided) return;

        if (!a.PlansSteps)
        {
            if (!CheckEnding(s, t, planner))
                Present(s, t, planner.FreePlay(s, FreePlayCheckSeconds), FreePlayCheckSeconds, newStep: true, act: SpeechAct.FreePlay,
                    tags: s.Retaliation.Active ? [$"reason:{s.Retaliation.Reason}"] : []);
            return;
        }

        var next = planner.Next(s, Now);
        t.Say.AddRange(next.Say);
        if (next.Ending)
        {
            if (CheckEnding(s, t, planner, due: true)) return;
            // Not yet: debts to collect first, or she is still punishing him. The plan runs a little longer.
            if (s.PendingConsequences.Count == 0) planner.Plans.Extend(s, a, Now, 0.06, "the ending waits");
            next = planner.Next(s, Now);
            t.Say.AddRange(next.Say);
            if (next.Ending)
            {
                // Nothing left to add: she ends it now; if even that must wait, a short rest.
                s.Retaliation.Active = false;
                if (!CheckEnding(s, t, planner, due: true)) Present(s, t, planner.Rest(s, 15), 15, newStep: true);
                return;
            }
        }
        PresentPlanned(s, t, planner, next);
    }

    private void PresentPlanned(SessionState s, Transition t, IPlanner planner, PlannedStep next)
    {
        if (next.Game is GameType type)
        {
            var stroke = planner.Stroke(s);
            s.EdgesThisStep = 0;
            var (game, first) = Games.Start(type, planner.GameContext(s, stroke, Now));
            s.Game = game;
            s.RecentGames.Add(type);
            if (s.RecentGames.Count > 5) s.RecentGames.RemoveAt(0);
            t.Effects.Add(new GameStarted(game));
            Present(s, t, first.Next! with { Segment = s.Plan?.Current?.Type }, first.TimerSeconds ?? 0, newStep: true, say: first.Say?.Tagged("start"));
            return;
        }
        var d = next.Directive!;
        var act = next.Tags.Contains("lube") ? SpeechAct.Lube : SpeechAct.Instruct;
        Present(s, t, d, next.TimerSeconds, newStep: true, act: act, tags: next.Tags);
    }

    /// <summary>
    /// She decides the ending: when the plan reaches it in a guided session, or when the points
    /// reach the budget in free play. The same rules and the same chance draw decide it in every mode.
    /// </summary>
    private bool CheckEnding(SessionState s, Transition t, IPlanner planner, bool due = false)
    {
        bool reached = s.Plan is null ? s.Points >= s.Budget : due;
        if (s.FinishDecided || s.Contract.GamesOnly || s.Game is not null || s.Retaliation.Active || !reached
            || s.Offer is not null || s.Price is not null) return false;
        var a = AuthoritiesFor(s, random);
        if (s.OwedEdges > 0)
        {
            // Debts come first: she collects the edges before she decides anything.
            if (a.PlansSteps && a.Edges.DommeSets)
            {
                if (!s.PendingConsequences.Any(x => x.Kind == ConsequenceKind.ExtraEdges))
                {
                    t.Say.Add(Utterance.Of(SpeechAct.Obligations, "collect-edges").With("owed", Stake.Plural(s.OwedEdges, "edge")));
                    s.PendingConsequences.Insert(0, new(ConsequenceKind.ExtraEdges, s.OwedEdges));
                }
            }
            else if (!s.OwedEdgesAnnounced)
            {
                s.OwedEdgesAnnounced = true;
                t.Say.Add(Utterance.Of(SpeechAct.Obligations, "collect-edges", "record").With("owed", Stake.Plural(s.OwedEdges, "edge")));
            }
            return false;
        }
        EndingAuthority.Refresh(s, s.Ending, projected: false);
        var lean = s.Ending;
        string told = lean.Told(orgasm: false);
        if (Blocked(s) is { } block)
        {
            s.FinishDecided = true;
            if (s.OwedEndings > 0 && PickEnder(s, orgasm: false) is { } denier) PresentEnder(s, t, planner, denier);
            else Present(s, t, planner.Denied(s), 0, newStep: true, act: SpeechAct.Denied, tags: [$"why:{block}", told]);
            return true;
        }
        if (s.OwedEndings > 0 && PickEnder(s, orgasm: true) is { } ender)
        {
            s.FinishDecided = true;
            PresentEnder(s, t, planner, ender);
            return true;
        }
        // An owed ruin ruins it anyway (and is served by it); her own choice to ruin applies otherwise.
        if (lean.Kind is EndingKind.Full or EndingKind.Ruined) Grant(s, t, planner, requested: false, ruinNow: lean.Kind == EndingKind.Ruined && s.OwedRuins == 0);
        else
        {
            s.FinishDecided = true;
            Present(s, t, planner.Denied(s), 0, newStep: true, act: SpeechAct.Denied, tags: [told, $"cause:{s.Mood.Cause}"]);
        }
        return true;
    }

    /// <summary>Permission to cum: an owed punishment ending takes its place, and an owed ruin ruins it.</summary>
    private void Grant(SessionState s, Transition t, IPlanner planner, bool requested, bool ruinNow = false)
    {
        s.FinishDecided = true;
        if (s.OwedEndings > 0 && PickEnder(s, orgasm: true) is { } ender)
        {
            PresentEnder(s, t, planner, ender);
            return;
        }
        s.PermissionGranted = true;
        s.FinishRuinOwed = !ruinNow && s.OwedRuins > 0 && s.RuinsAllowed;
        s.FinishRuined = ruinNow || s.FinishRuinOwed;
        var finishing = planner.Finishing(s, s.FinishRuined);
        var tags = new List<string> { requested ? "requested" : "her-decision", s.Ending.Told(orgasm: true) };
        if (s.FinishRuined) tags.Add("ruined");
        Present(s, t, finishing, finishing.DurationOrCount, newStep: true, act: SpeechAct.Finish, tags: [.. tags]);
    }

    private void GrantReward(SessionState s, Transition t, IPlanner planner, OfferBuys reward, bool ruinNow = false)
    {
        switch (reward)
        {
            case OfferBuys.Orgasm:
                Grant(s, t, planner, requested: true, ruinNow);
                break;
            case OfferBuys.Rest:
                Rest(s, t);
                break;
            default:
                t.Say.Add(Utterance.Of(SpeechAct.MercyGranted));
                Advance(s, t, completed: false);
                break;
        }
    }

    private void PayPrice(SessionState s, Transition t, IPlanner planner)
    {
        var price = s.Price!;
        s.Price = null;
        t.Say.Add(Utterance.Of(SpeechAct.PricePaid, $"buys:{price.Reward}"));
        GrantReward(s, t, planner, price.Reward);
    }

    private void Rest(SessionState s, Transition t)
    {
        Accrue(s);
        s.Suspended = null;
        s.Holding = false;
        var rest = Planner(AuthoritiesFor(s, random)).Rest(s, 15 + random.Next(0, 16));
        s.PreviousCategory = StepCategory.Rest;
        s.StepsSinceRest = 0;
        Present(s, t, rest, rest.DurationOrCount, newStep: true, tags: "granted");
    }

    private void PresentEnder(SessionState s, Transition t, IPlanner planner, EnderInfo ender)
    {
        s.FinishEnderId = ender.Id;
        s.PermissionGranted = ender.EndsInOrgasm;
        var d = planner.PunishmentEnding(s, ender);
        var say = Utterance.Instruct(d, SpeechAct.PunishmentEnding, ender.EndsInOrgasm ? "orgasm" : "denied")
            .With("ender", $"{ender.Announcement} {ender.Instruction}".Trim());
        Present(s, t, d, d.Kind == InstructionKind.Finish ? 0 : d.DurationOrCount, newStep: true, say: say);
    }

    private EnderInfo? PickEnder(SessionState s, bool orgasm)
    {
        var pool = s.Enders.Where(x => orgasm || !x.EndsInOrgasm).ToList();
        if (pool.Count == 0) return null;
        return random.Weighted(pool.Select(x => (x, 1.0 + Math.Max(0, 10 - Math.Abs(x.Severity - (3 + s.Mean * 0.7))))).ToList(), pool[0]);
    }

    /// <summary>What finishing served: an owed ruin or an owed punishment ending.</summary>
    private static void ServeFinish(SessionState s)
    {
        if (s.FinishEnderId is not null && s.OwedEndings > 0)
        {
            s.OwedEndings--;
            s.EndingsServed++;
        }
        if (s.FinishRuinOwed && s.OwedRuins > 0)
        {
            s.OwedRuins--;
            s.RuinsServed++;
        }
    }

    private static void PayOwedEdge(SessionState s, Transition t)
    {
        if (s.OwedEdges <= 0) return;
        s.OwedEdges--;
        s.EdgesPaid++;
        if (s.OwedEdges == 0) t.Say.Add(Utterance.Of(SpeechAct.DebtPaid));
    }

    private bool MayOffer(SessionState s, OfferBuys buys)
    {
        if (s.OfferTemplates.Count == 0 || s.Contract.GamesOnly) return false;
        if (buys == OfferBuys.Orgasm && (s.Progress < 0.2 || s.Retaliation is { Active: true, Reason: RetaliationReason.UnauthorizedOrgasm })) return false;
        double chance = buys switch
        {
            OfferBuys.Orgasm => 30 + 12 * Math.Max(0, s.RequestsThisSession - 1),
            OfferBuys.Rest => 35,
            _ => 30,
        };
        chance *= s.Mood.Mood switch { DommeMood.Playful => 1.4, DommeMood.Teasing => 1.2, DommeMood.Cruel => 0.8, _ => 1 };
        return random.Chance(Math.Min(75, chance));
    }

    private void Put(SessionState s, Transition t, PendingOffer offer)
    {
        s.Offer = offer;
        t.Effects.Add(new OfferMade(offer));
    }

    private static void Decide(SessionState s, Transition t, OfferStatus status)
    {
        var offer = s.Offer!;
        t.Effects.Add(new OfferDecided(offer, status));
        if (status is OfferStatus.Declined or OfferStatus.Expired) s.RefusedOffers.Add(new(offer.Buys, offer.TemplateId, offer.Points));
        s.Offer = null;
    }

    /// <summary>Her words for an offer: whether her price went up since his last no, and whether it's the same deal.</summary>
    private static Utterance OfferLine(PendingOffer offer)
    {
        var line = Utterance.Of(SpeechAct.OfferMade, $"buys:{offer.Buys.ToString().ToLowerInvariant()}", $"flavor:{offer.Flavor}").With("offer", offer.Text);
        if (offer.Raised) line = line.Tagged("raised");
        if (offer.Raised && offer.SameAsRefused) line = line.Tagged("same");
        return line;
    }

    /// <summary>Orgasm and rest offers lapse quietly; a mercy offer waits on its own timer.</summary>
    private void ExpireOffer(SessionState s, Transition t)
    {
        if (s.Offer is { Buys: not OfferBuys.Mercy } offer && Now >= offer.ExpiresAtUtc) Decide(s, t, OfferStatus.Expired);
    }

    private static string Bought(OfferBuys buys) => buys switch { OfferBuys.Orgasm => "an orgasm", OfferBuys.Rest => "a rest", _ => "mercy" };

    private static void QueueStakes(SessionState s, IEnumerable<Consequence> stakes)
    {
        foreach (var consequence in stakes)
        {
            switch (consequence.Kind)
            {
                case ConsequenceKind.Points: s.Points = Math.Max(0, s.Points + consequence.Amount); break;
                case ConsequenceKind.PermissionOdds: s.PermissionModifier += consequence.Amount; break;
                default: s.PendingConsequences.Add(consequence); break;
            }
        }
    }

    /// <summary>What she reminds him of at the start: promises in force and what he owes.</summary>
    private Utterance? ObligationLine(SessionState s)
    {
        var tags = new List<string>();
        var u = Utterance.Of(SpeechAct.Obligations);
        if (s.DenialUntilUtc is DateTime until && Now < until)
        {
            tags.Add(until == DateTime.MaxValue ? "no-orgasm-tonight" : "in-denial");
            if (Enum.TryParse<Block>(s.DenialReason, true, out var why)) tags.Add($"why:{why}");
            if (until != DateTime.MaxValue) u = u.With("left", Left(until - Now));
        }
        if (s.OwedEdges > 0) { tags.Add("owed-edges"); u = u.With("owed", Stake.Plural(s.OwedEdges, "edge")); }
        if (s.OwedEndings > 0) tags.Add("owed-ending");
        if (s.OwedRuins > 0) { tags.Add("owed-ruins"); u = u.With("count", s.OwedRuins); }
        var traits = s.Contract.Obligations.Character;
        if (traits.Long || traits.Intense || traits.Torturous || traits.Mean || traits.Teasing) tags.Add("promised-character");
        return tags.Count == 0 ? null : u.Tagged([.. tags]);
    }

    private void HandleGame(SessionState s, Transition t, IPlanner planner, GameStep step)
    {
        if (step.Result is { } result)
        {
            var game = s.Game!;
            s.Game = null;
            t.Effects.Add(new GameFinished(game, result));
            t.Say.Add(result.Say);
            if (!result.Neutral)
            {
                if (result.Won) { s.GamesWon++; Mood.Nudge(s, random, -0.4, MoodCause.WonGame); }
                else { s.GamesLost++; Mood.Retaliate(s, random, RetaliationReason.LostGame, 2, 5, 0.6); }
            }
            // A lost game can be bought off: she names a price instead of the punishment.
            var hurts = result.Consequences.Where(Hurts).ToList();
            if (!result.Won && hurts.Count > 0 && MayOffer(s, OfferBuys.Mercy)
                && Offers.Make(s, OfferBuys.Mercy, random, Now, hurts) is { } offer)
            {
                QueueStakes(s, result.Consequences.Where(x => !Hurts(x)));
                Put(s, t, offer);
                var d = planner.OfferDecision(s, offer);
                Present(s, t, d, Offers.ExpirySeconds, newStep: true,
                    say: Utterance.Instruct(d, SpeechAct.OfferDecision, "buys:mercy", $"flavor:{offer.Flavor}").With("offer", offer.Text));
                return;
            }
            QueueStakes(s, result.Consequences);
            Advance(s, t, completed: true);
            return;
        }
        var next = step.Next! with { Segment = s.Current?.Segment };
        if (step.TimerSeconds is int seconds) Present(s, t, next, seconds, newStep: false, say: step.Say);
        else
        {
            // Same timer keeps running; only the instruction changes.
            int remaining = TimerRemaining(s);
            Present(s, t, next with { DurationOrCount = remaining }, remaining, newStep: false, keepTimer: true, say: step.Say);
        }
    }

    private static bool Hurts(Consequence c) => c.Kind switch
    {
        ConsequenceKind.ExtraEdges or ConsequenceKind.Torment => true,
        ConsequenceKind.Points or ConsequenceKind.PermissionOdds => c.Amount < 0,
        _ => false,
    };

    private void AbandonGame(SessionState s, Transition t)
    {
        // Leaving a game early (mode change, ending) never fabricates a loss.
        t.Effects.Add(new GameFinished(s.Game!, null));
        s.Game = null;
    }

    /// <summary>
    /// Makes a directive current: metronome tempo, points bookkeeping, timer, and what she says
    /// (the instruction line, unless a hidden free-play check-in continues quietly).
    /// </summary>
    private void Present(SessionState s, Transition t, Directive d, int timerSeconds, bool newStep, bool keepTimer = false,
        SpeechAct act = SpeechAct.Instruct, Utterance? say = null, params string[] tags)
    {
        var a = AuthoritiesFor(s, random);
        bool stimulating = Stimulating(d);
        d = d with
        {
            Tempo = stimulating && d.Category != StepCategory.Hold ? a.Pacing.Tempo(d.Speed) : 0,
            DurationOrCount = d.Kind == InstructionKind.FreeText && d.DurationOrCount == 0 && timerSeconds > 0 ? timerSeconds : d.DurationOrCount,
            Text = "",
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
        if (stimulating && d.Method is { IsUserChoice: false } m && (s.RecentMethodIds.Count == 0 || s.RecentMethodIds[^1] != m.Id))
        {
            s.RecentMethodIds.Add(m.Id);
            if (s.RecentMethodIds.Count > 10) s.RecentMethodIds.RemoveAt(0);
        }
        if (!keepTimer) SetTimer(s, timerSeconds);
        if (d.HiddenTimer && !newStep) return;
        var line = (say ?? Utterance.Instruct(d, act)) with { Directive = d, Instruction = true };
        if (d.Segment is { } segment) line = line.Tagged($"segment:{segment}");
        t.Say.Add(tags.Length > 0 ? line.Tagged(tags) : line);
    }

    private static MethodInfo GameMethod(SessionState s, GameState game) => game.Method ?? MethodInfo.UserChoice(s.Methods);

    private static bool Stimulating(Directive? d)
        => d is { Method: not null } && d.Category is not (StepCategory.Rest or StepCategory.Hold or StepCategory.Offer) && d.Kind != InstructionKind.Finish;

    /// <summary>Converts stimulation since the last accrual into points (free play stops counting when unreported).</summary>
    private void Accrue(SessionState s, DateTime? until = null)
    {
        Feel(s, until);
        if (s.ActiveSinceUtc is not DateTime since || s.Paused) return;
        var end = until ?? Now;
        if (!AuthoritiesFor(s, random).Pacing.DommeSets)
        {
            var cutoff = s.LastActivityUtc + Points.MaxUnreportedStimulation;
            if (cutoff < end) end = cutoff;
        }
        if (end > since)
        {
            // The method's own worth, then what his state makes of it: seconds near the edge count for more.
            var method = s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods);
            double plain = Points.ForStimulation(method, (end - since).TotalSeconds, s.ActiveSpeed);
            double earned = plain * Points.StateFactor(s.Body.Closeness, method);
            s.Points += earned;
            if (method.TortLevel >= Points.Torturous) s.Earned.Torture += plain; else s.Earned.Stroking += plain;
            s.Earned.Close += earned - plain;
        }
        s.ActiveSinceUtc = until ?? Now;
    }

    /// <summary>What his body is doing right now, as far as the engine knows.</summary>
    public static BodyMode ModeOf(SessionState s)
        => s.Paused || s.Current is null ? BodyMode.Rest
            : s.Holding || s.Current.Category == StepCategory.Hold ? BodyMode.Hold
            : s.ActiveSinceUtc is not null ? BodyMode.Stroke
            : BodyMode.Rest;

    /// <summary>How far her estimate of his closeness may rise in the current step without an edge being reported.</summary>
    public static double CapOf(SessionState s, Authorities a)
    {
        if (!a.Pacing.DommeSets) return Body.SelfPacedCap;
        var d = s.Current;
        bool edgeExpected = d is { Kind: InstructionKind.EdgeCount } or { CountEdges: true } || s.Price is not null
            || s.Game is { Type: GameType.RaceToEdge or GameType.EdgeRush or GameType.EdgeEndurance or GameType.EdgeWager };
        return edgeExpected ? Body.EstimateCap : Body.TimedCap;
    }

    /// <summary>Her estimate of his state as of now, without changing the session.</summary>
    public BodyState Estimate(SessionState s)
    {
        var b = s.Body.Copy();
        if (b.AsOfUtc is DateTime since && (Now - since).TotalSeconds > 0)
            Body.Advance(b, s.Profile, ModeOf(s), s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods), s.ActiveSpeed, (Now - since).TotalSeconds,
                CapOf(s, AuthoritiesFor(s, random)));
        return b;
    }

    /// <summary>Brings her estimate of his state up to now (or <paramref name="until"/>), in whatever he has been doing since.</summary>
    private void Feel(SessionState s, DateTime? until = null)
    {
        var b = s.Body;
        var now = until ?? Now;
        if (b.AsOfUtc is not DateTime since) { b.AsOfUtc = now; return; }
        double seconds = (now - since).TotalSeconds;
        if (seconds <= 0) return;
        var a = AuthoritiesFor(s, random);
        var mode = ModeOf(s);
        // When he sets the pace and has gone quiet, she stops assuming he is still stroking.
        if (mode == BodyMode.Stroke && !a.Pacing.DommeSets && since >= s.LastActivityUtc + Points.MaxUnreportedStimulation) mode = BodyMode.Rest;
        Body.Advance(b, s.Profile, mode, s.ActiveMethod ?? MethodInfo.UserChoice(s.Methods), s.ActiveSpeed, seconds, CapOf(s, a));
        b.AsOfUtc = now;
    }

    private void SetTimer(SessionState s, int seconds)
    {
        s.TimerGeneration++;
        s.TimerDueUtc = seconds > 0 ? Now.AddSeconds(seconds) : null;
    }

    private int TimerRemaining(SessionState s)
        => s.TimerDueUtc is DateTime due ? Math.Max(0, (int)Math.Ceiling((due - Now).TotalSeconds)) : 0;

    private static Transition Reject(Transition t, string reason)
    {
        t.Rejected = reason;
        return t;
    }
}
