using AIDome.Data;
using AIDome.Sessions;
using AIDome.Speech;
using Xunit;

namespace AIDome.Tests;

/// <summary>Obligations, deals and interruptions in the session engine, and the rules behind them.</summary>
public sealed class LedgerEngineTests
{
    private static readonly SessionMode[] Presets = [SessionMode.FullGuided, SessionMode.OwnContent, SessionMode.OwnMethods, SessionMode.FreePlay];

    private static readonly OfferTemplate EarnIt = new()
    {
        Id = 1, Name = "Earn it", Wording = "Edge [amount] more time[s], then you may [what].", Kind = OfferKind.EdgesNow,
        MinAmount = 3, MaxAmount = 10, PointsPerUnit = 4, Flavor = OfferFlavor.Strict, ForOrgasm = true,
    };
    /// <summary>Deals that can cost what an orgasm is worth early in a session: she no longer sells one for less.</summary>
    private static OfferTemplate EarnItDear => EarnIt with { MaxAmount = 60 };
    private static OfferTemplate PayInDaysDear => PayInDays with { MaxAmount = 30 };
    private static readonly OfferTemplate PayInDays = new()
    {
        Id = 3, Name = "Pay in days", Wording = "You may [what], for [amount] day[s] of denial.", Kind = OfferKind.Denial,
        MinAmount = 1, MaxAmount = 7, PointsPerUnit = 14, Flavor = OfferFlavor.Strict, ForOrgasm = true, ForRest = true, ForMercy = true,
    };
    private static readonly EnderInfo Forced = new() { Id = 1, Name = "Forced", Announcement = "You're going to cum.", Instruction = "Full speed through it.", Severity = 7 };
    private static readonly EnderInfo Marathon = new() { Id = 11, Name = "Marathon", Announcement = "Five more edges.", Instruction = "Then walk away.", Severity = 5, EndsInOrgasm = false };

    private static SessionState State(SessionMode mode, SessionObligations? owed = null, params OfferTemplate[] offers)
    {
        var s = SessionEngineTests.NewState(mode, methods: SessionEngineTests.Palm);
        s.Contract = s.Contract with { Obligations = owed ?? new() };
        s.OfferTemplates = offers.ToList();
        return s;
    }

    /// <summary>Runs until she decides the ending: timers in guided modes, quiet check-ins in free play.</summary>
    private static void ToEnding(SessionEngine engine, SessionState s, FakeClock clock)
    {
        for (int i = 0; i < 200 && s.Active && !s.FinishDecided; i++)
        {
            if (s.Current?.Category == StepCategory.FreePlay && s.TimerDueUtc is DateTime due)
            {
                clock.UtcNow = due;
                engine.Timer(s, s.TimerGeneration);
            }
            else SessionEngineTests.Drive(engine, s, clock, 1);
        }
    }

    [Fact]
    public void APromisedDenial_BlocksPermission_AndHerEnding_InEveryMode()
    {
        foreach (var mode in Presets)
        {
            var clock = new FakeClock();
            var engine = new SessionEngine(new FixedRandom(0.01), clock);
            var s = State(mode, new() { NoOrgasmUntilUtc = DateTime.MaxValue, NoOrgasmReason = "Promise" });
            var begin = engine.Begin(s);
            Assert.Contains(begin.Say, u => u.Act == SpeechAct.Obligations && u.Has("no-orgasm-tonight") && u.Has("why:Promise"));
            s.Points = s.Budget * 0.9;
            var asked = engine.RequestPermission(s);
            Assert.Contains(asked.Say, u => u.Act == SpeechAct.Blocked && u.Has("why:Promise") && u.Has("this-session"));
            Assert.Equal(0, Assert.IsType<PermissionAnswered>(Assert.Single(asked.Effects)).Chance);
            Assert.False(s.PermissionGranted);
            s.Points = s.Budget;
            ToEnding(engine, s, clock);
            Assert.True(s.FinishDecided, mode.ToString());
            Assert.False(s.PermissionGranted);
            Assert.Equal(InstructionKind.Finish, s.Current!.Kind);
        }
    }

    [Fact]
    public void OwedEdges_ArePaidByAnyEdge_AndCollectedBeforeSheDecides()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.01), clock);
        var s = State(SessionMode.FullGuided, new() { OwedEdges = 3 });
        Assert.Contains(engine.Begin(s).Say, u => u.Act == SpeechAct.Obligations && u.Slots["owed"] == "3 edges");
        Assert.Contains(engine.RequestPermission(s).Say, u => u.Act == SpeechAct.Blocked && u.Has("why:OwedEdges") && u.Slots["owed"] == "3 edges");
        engine.Edge(s);
        engine.Edge(s);
        var last = engine.Edge(s);
        Assert.Equal((0, 3), (s.OwedEdges, s.EdgesPaid));
        Assert.Contains(last.Say, u => u.Act == SpeechAct.DebtPaid);

        // Free play: at the budget she names the debt once and waits for the edges.
        var free = State(SessionMode.FreePlay, new() { OwedEdges = 2 });
        engine.Begin(free);
        free.Points = free.Budget;
        clock.Advance(SessionEngine.FreePlayCheckSeconds);
        var named = engine.Timer(free, free.TimerGeneration);
        Assert.Contains(named.Say, u => u.Act == SpeechAct.Obligations && u.Has("collect-edges") && u.Has("record"));
        Assert.False(free.FinishDecided);
        engine.Edge(free);
        Assert.False(free.FinishDecided);
        engine.Edge(free);
        Assert.True(free.FinishDecided);
    }

    [Fact]
    public void AnOfferCostsTheSame_AndIsPaidTheSameWay_InEveryMode()
    {
        var amounts = new List<int>();
        foreach (var mode in new[] { SessionMode.FullGuided, SessionMode.OwnMethods, SessionMode.FreePlay })
        {
            var clock = new FakeClock();
            var engine = new SessionEngine(new FixedRandom(0.1), clock);
            var s = State(mode, offers: EarnItDear);
            engine.Begin(s);
            s.Points = s.Budget * 0.3;
            var asked = engine.RequestPermission(s);
            var offer = Assert.Single(asked.Effects.OfType<OfferMade>()).Offer;
            amounts.Add(offer.Amount);
            Assert.Contains(asked.Say, u => u.Act == SpeechAct.OfferMade && u.Slots["offer"] == offer.Text);
            Assert.Equal(OfferStatus.Accepted, Assert.Single(engine.AcceptOffer(s).Effects.OfType<OfferDecided>()).Status);
            Assert.Equal(offer.Amount, s.Price!.EdgesLeft);
            Assert.NotNull(engine.RequestPermission(s).Rejected);
            for (int i = 0; i < offer.Amount - 1; i++)
            {
                engine.Edge(s);
                Assert.False(s.PermissionGranted, mode.ToString());
            }
            engine.Edge(s);
            Assert.True(s.PermissionGranted, mode.ToString());
            Assert.Equal(StepCategory.Finish, s.Current!.Category);
        }
        Assert.Single(amounts.Distinct());
    }

    [Fact]
    public void ARefusedOffer_MakesHerNextOfferDearer_AndSheSaysSo()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.1), clock);
        var s = State(SessionMode.FullGuided, offers: [EarnItDear, PayInDaysDear]);
        engine.Begin(s);
        s.Points = s.Budget * 0.3;
        var first = Offers.Make(s, OfferBuys.Orgasm, engine.Random, clock.UtcNow)!;
        s.Offer = first;
        engine.DeclineOffer(s);
        Assert.Single(s.RefusedOffers);
        var asked = engine.RequestPermission(s);
        var second = Assert.Single(asked.Effects.OfType<OfferMade>()).Offer;
        Assert.Equal(1, second.AfterRefusals);
        Assert.Contains(asked.Say, u => u.Act == SpeechAct.OfferMade && u.Has("raised"));
        Assert.True(second.Points > first.Points, $"{second.Name} {second.Points} after refusing {first.Name} {first.Points}");
        // A lapsed offer counts as a no too.
        clock.UtcNow = second.ExpiresAtUtc.AddSeconds(1);
        engine.Edge(s);
        Assert.Equal(2, s.RefusedOffers.Count);
        Assert.Equal(2, Offers.Make(s, OfferBuys.Orgasm, engine.Random, clock.UtcNow)!.AfterRefusals);
    }

    [Fact]
    public void ADealStruckMidHold_EndsTheHold_SoHeCanPay()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.1), clock);
        var s = State(SessionMode.FullGuided, offers: EarnItDear);
        engine.Begin(s);
        s.Points = s.Budget * 0.3;
        s.Holding = true;
        engine.RequestPermission(s);
        engine.AcceptOffer(s);
        Assert.False(s.Holding);
        int left = s.Price!.EdgesLeft;
        engine.Edge(s);
        Assert.Equal(left - 1, s.Price?.EdgesLeft ?? 0);
    }

    [Fact]
    public void APriceForARest_AppliesNow_ButAPriceForAnOrgasm_StartsAfterIt()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.5), clock);
        var rest = State(SessionMode.FullGuided, offers: PayInDays);
        engine.Begin(rest);
        rest.Offer = Offers.Make(rest, OfferBuys.Rest, engine.Random, clock.UtcNow);
        var t = engine.AcceptOffer(rest);
        var owed = Assert.Single(t.Effects.OfType<LedgerOwed>());
        Assert.Equal((LedgerKind.Denial, LedgerOrigin.Offer), (owed.Stake.Kind, owed.Origin));
        Assert.Equal(clock.UtcNow.AddDays(owed.Stake.Amount), rest.DenialUntilUtc);
        Assert.Equal(InstructionKind.Rest, rest.Current!.Kind);

        var orgasm = State(SessionMode.FreePlay, offers: PayInDays);
        engine.Begin(orgasm);
        orgasm.Offer = Offers.Make(orgasm, OfferBuys.Orgasm, engine.Random, clock.UtcNow);
        t = engine.AcceptOffer(orgasm);
        Assert.Single(t.Effects.OfType<LedgerOwed>());
        Assert.Null(orgasm.DenialUntilUtc);
        Assert.True(orgasm.PermissionGranted);
    }

    [Fact]
    public void AMercyOffer_ReplacesAGameLoss_OrTheLossStands()
    {
        (SessionEngine, SessionState, FakeClock) Setup()
        {
            var clock = new FakeClock();
            var engine = new SessionEngine(new FixedRandom(0.5), clock);
            var s = State(SessionMode.FullGuided, offers: PayInDays);
            engine.Begin(s);
            s.Offer = new()
            {
                Id = Guid.NewGuid(), Buys = OfferBuys.Mercy, Kind = OfferKind.Denial, Amount = 1, Severity = 3, Text = "A day of denial instead.",
                ExpiresAtUtc = clock.UtcNow.AddSeconds(Offers.ExpirySeconds), Replaces = [new(ConsequenceKind.ExtraEdges, 4)],
            };
            s.Current = new() { Kind = InstructionKind.FreeText, Category = StepCategory.Offer, DurationOrCount = Offers.ExpirySeconds };
            s.TimerDueUtc = clock.UtcNow.AddSeconds(Offers.ExpirySeconds);
            return (engine, s, clock);
        }

        var (engine, s, clock) = Setup();
        var accepted = engine.AcceptOffer(s);
        Assert.Single(accepted.Effects.OfType<LedgerOwed>());
        Assert.Contains(accepted.Say, u => u.Act == SpeechAct.MercyGranted);
        Assert.Empty(s.PendingConsequences);

        (engine, s, clock) = Setup();
        engine.DeclineOffer(s);
        Assert.Equal((InstructionKind.EdgeCount, 4), (s.Current!.Kind, s.Current.DurationOrCount));

        (engine, s, clock) = Setup();
        clock.Advance(Offers.ExpirySeconds);
        var ignored = engine.Timer(s, s.TimerGeneration);
        Assert.Equal(OfferStatus.Expired, Assert.Single(ignored.Effects.OfType<OfferDecided>()).Status);
        Assert.Equal((InstructionKind.EdgeCount, 4), (s.Current!.Kind, s.Current.DurationOrCount));
    }

    [Fact]
    public void AnOwedPunishmentEnding_TakesHerEnding_AndOnlyADenyingOneServesDuringDenial()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.01), clock);
        var s = State(SessionMode.FullGuided, new() { PunishmentEnding = true });
        s.Enders = [Forced, Marathon];
        engine.Begin(s);
        s.Points = s.Budget;
        ToEnding(engine, s, clock);
        Assert.NotNull(s.FinishEnderId);
        if (s.PermissionGranted) engine.Orgasm(s, allowed: true);
        else engine.Complete(s);
        Assert.Equal((1, 0), (s.EndingsServed, s.OwedEndings));

        var denied = State(SessionMode.FreePlay, new() { PunishmentEnding = true, NoOrgasmUntilUtc = DateTime.MaxValue });
        denied.Enders = [Forced, Marathon];
        engine.Begin(denied);
        denied.Points = denied.Budget;
        ToEnding(engine, denied, clock);
        Assert.Equal(Marathon.Id, denied.FinishEnderId);
        Assert.False(denied.PermissionGranted);
        engine.Complete(denied);
        Assert.Equal(1, denied.EndingsServed);
    }

    [Fact]
    public void AnOwedRuin_RuinsTheNextPermittedOrgasm()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.01), clock);
        var s = State(SessionMode.FreePlay, new() { OwedRuins = 1 });
        engine.Begin(s);
        s.Points = s.Budget;
        ToEnding(engine, s, clock);
        Assert.True(s.PermissionGranted);
        Assert.True(s.FinishRuined);
        // Her words for it say so.
        var line = RuleNarrator.Default.Render(Utterance.Instruct(s.Current!, SpeechAct.Finish, "her-decision", "ruined"),
            Simulation.Context(s, null, new FixedRandom(0.5), clock.UtcNow))!;
        Assert.Contains("let go completely", line.Text);
        engine.Orgasm(s, allowed: true);
        Assert.Equal((0, 1), (s.OwedRuins, s.RuinsServed));
    }

    [Fact]
    public void AnInterruption_StopsAsOfTheLastVisit_AndWaitsForAnAnswerBeforeResuming()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.5), clock);
        var s = State(SessionMode.FreePlay);
        engine.Begin(s);
        clock.Advance(30);
        var seen = clock.UtcNow;
        clock.Advance(600);
        engine.Interrupt(s, seen);
        Assert.True(s.Paused);
        Assert.Equal(seen, s.InterruptedAtUtc);
        Assert.Equal(seen, s.PausedAtUtc);
        Assert.NotNull(engine.Resume(s).Rejected);
        engine.ClearInterruption(s);
        Assert.Null(engine.Resume(s).Rejected);
        Assert.False(s.Paused);
    }

    [Fact]
    public void Commitments_SheCountersBrokenTrust_AndAlwaysNamesTheStakes()
    {
        var distrust = CommitmentRules.Respond(new() { Scope = CommitmentScope.Days, Days = 4, NoOrgasm = true }, 5, 1, new FixedRandom(0.5));
        Assert.True(distrust.Countered);
        Assert.Equal(6, distrust.Terms.Days);
        Assert.Contains(distrust.Stakes, x => x.Kind == LedgerKind.Denial && x.Amount >= 3);
        Assert.Equal(SpeechAct.PromiseAnswer, distrust.Say.Act);
        Assert.True(distrust.Say.Has("distrust") && distrust.Say.Has("countered"));
        Assert.Equal(Stake.Describe(distrust.Stakes), distrust.Say.Slots["stakes"]);

        var trusted = CommitmentRules.Respond(new() { NoOrgasm = true }, 1, 0, new FixedRandom(0.99));
        Assert.False(trusted.Countered);
        Assert.Equal("no orgasm this session", trusted.Terms.Describe());
        Assert.Equal(new Stake(LedgerKind.Denial, 2, 3), Assert.Single(trusted.Stakes));

        var character = CommitmentRules.Respond(new() { Scope = CommitmentScope.Days, Days = 3, Long = true }, 9, 0, new FixedRandom(0.01));
        Assert.True(character.Countered);
        Assert.True(character.Terms.HasCharacter && character.Terms != new CommitmentTerms { Scope = CommitmentScope.Days, Days = 3, Long = true });
        Assert.Contains(character.Stakes, x => x.Kind == LedgerKind.PunishmentSession);

        Assert.Throws<ArgumentException>(() => new CommitmentTerms().Validate());
        Assert.Throws<ArgumentException>(() => new CommitmentTerms { Scope = CommitmentScope.Days, NoOrgasm = true }.Validate());
    }

    [Fact]
    public void Breaches_OnPurposeCostMoreThanAccidents()
    {
        static int Weight(IEnumerable<Stake> stakes) => stakes.Sum(x => x.Severity * (x.Kind == LedgerKind.Edges ? 1 : x.Amount));
        Assert.True(Weight(BreachRules.ForOrgasm(true, 5, false)) > Weight(BreachRules.ForOrgasm(false, 5, false)));
        Assert.True(Weight(BreachRules.ForOrgasm(false, 5, true)) > Weight(BreachRules.ForOrgasm(false, 5, false)));
        var agreed = new[] { new Stake(LedgerKind.Denial, 2, 4) };
        Assert.Equal(agreed, BreachRules.ForCommitment(agreed, intentional: false));
        Assert.Contains(BreachRules.ForCommitment(agreed, intentional: true), x => x.Kind == LedgerKind.PunishmentSession);
    }

    [Fact]
    public void OffersAreValuedInSessionPoints_AndFilledToMatch()
    {
        var s = State(SessionMode.FreePlay, offers: [EarnIt, PayInDays]);
        s.Budget = 100;
        s.Points = 20;
        double early = Offers.Value(s, OfferBuys.Orgasm);
        s.Points = 95;
        Assert.True(Offers.Value(s, OfferBuys.Orgasm) < early);
        Assert.Equal((5, 0), Offers.Fill(EarnIt, 20, new FixedRandom(0.5)));
        Assert.Equal((10, 0), Offers.Fill(EarnIt, 400, new FixedRandom(0.5)));
        Assert.Equal(16, Offers.Value(s, OfferBuys.Mercy, [new(ConsequenceKind.ExtraEdges, 4), new(ConsequenceKind.Rest, 30)]));
        Assert.Equal("Edge 1 more time, then you may cum.", Offers.Words(EarnIt, 1, 0, OfferBuys.Orgasm));
    }
}
