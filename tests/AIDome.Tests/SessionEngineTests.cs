using System.Text.Json;
using AIDome.Data;
using AIDome.Persistence;
using AIDome.Sessions;
using Xunit;

namespace AIDome.Tests;

/// <summary>Always returns the same fraction: every roll is predictable.</summary>
public sealed class FixedRandom(double value) : IRandom
{
    public double Value { get; set; } = value;
    public int Next(int min, int max) => max <= min ? min : Math.Min(max - 1, min + (int)(Value * (max - min)));
    public double NextDouble() => Value;
}

public sealed class SeededRandom(int seed) : IRandom
{
    private readonly Random random = new(seed);
    public int Next(int min, int max) => random.Next(min, max);
    public double NextDouble() => random.NextDouble();
}

public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 9, 22, 20, 0, 0, DateTimeKind.Utc);
    public void Advance(double seconds) => UtcNow = UtcNow.AddSeconds(seconds);
}

public sealed class SessionEngineTests
{
    private static readonly MethodInfo Palm = new()
    {
        Id = 7, Name = "Palm", Description = "Palm the head", Intensity = 5, Tease = 2, TortLevel = 1, Closeness = 6,
        TimePoints = 0.1, EdgePoints = 3,
    };
    private static readonly MethodInfo Grip = Palm with { Id = 8, Name = "Grip", Description = "Full grip", Closeness = 7, TimePoints = 0.12, EdgePoints = 3.4 };

    private static SessionState NewState(SessionMode mode, Action<SessionContract>? _ = null, SessionContract? contract = null,
        params MethodInfo[] methods)
    {
        var c = (contract ?? SessionContract.Preset(mode) with
        {
            Preferences = new() { Intensity = 5, Length = 5, Denial = 5, Mean = 5 },
        }).Validate();
        return new SessionState { SessionId = 1, Contract = c, Methods = methods.Length > 0 ? methods.ToList() : [Palm, Grip] };
    }

    /// <summary>Runs the session forward: fires timers and reaches edge targets until the budget or a step limit.</summary>
    private static void Drive(SessionEngine engine, SessionState s, FakeClock clock, int steps)
    {
        for (int i = 0; i < steps && s.Active && !s.FinishDecided; i++)
        {
            if (s.Game is { } game)
            {
                if (game.Input != GameInput.None && game.Choices.Count > 0) engine.Answer(s, game.Choices[0].Value);
                else if (game.Input == GameInput.Number) engine.Answer(s, 1);
                else if (s.TimerDueUtc is DateTime gameDue) { clock.UtcNow = gameDue; engine.Timer(s, s.TimerGeneration); }
                else engine.Edge(s);
                continue;
            }
            if (s.Current?.Kind == InstructionKind.EdgeCount && !s.Holding) { engine.Edge(s); continue; }
            if (s.TimerDueUtc is DateTime due) { clock.UtcNow = due; engine.Timer(s, s.TimerGeneration); continue; }
            if (s.PendingLube is not null) { engine.Complete(s); continue; }
            engine.Edge(s);
        }
    }

    [Fact]
    public void Presets_MapToTheAuthorityMatrix_AndValidationKeepsGamesWithDommePacing()
    {
        var full = SessionContract.Preset(SessionMode.FullGuided);
        Assert.Equal((Authority.Domme, Authority.Domme, Authority.Domme, Authority.Domme), (full.Content, full.Methods, full.Pacing, full.Edges));
        var own = SessionContract.Preset(SessionMode.OwnMethods);
        Assert.Equal((Authority.User, Authority.User, Authority.Domme, Authority.Domme), (own.Content, own.Methods, own.Pacing, own.Edges));
        var free = SessionContract.Preset(SessionMode.FreePlay);
        Assert.Equal((Authority.User, Authority.User, Authority.User, Authority.User, false), (free.Content, free.Methods, free.Pacing, free.Edges, free.Games));
        Assert.Throws<ArgumentException>(() => (free with { Games = true }).Validate());
        Assert.Equal(SessionMode.Custom, (full with { Methods = Authority.User }).Validate().Mode);
        Assert.Equal(SessionMode.OwnContent, (full with { Content = Authority.User, Mode = SessionMode.FullGuided }).Validate().Mode);
    }

    [Fact]
    public void FullGuided_PrescribesMethodSpeedAndTempo()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new SeededRandom(3), clock);
        var s = NewState(SessionMode.FullGuided);
        var t = engine.Begin(s);
        Assert.NotEmpty(t.Domme);
        Assert.Equal(1, s.StepNumber);
        Assert.NotNull(s.Current!.Method);
        Assert.False(s.Current.Method!.IsUserChoice);
        Assert.InRange(s.Current.Speed, 1, 10);
        Assert.Equal(Tempo.FromSpeed(s.Current.Speed), s.Current.Tempo);
    }

    [Fact]
    public void OwnMethods_NeverPrescribesAMethod_ButStillSetsThePace()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new SeededRandom(11), clock);
        var s = NewState(SessionMode.OwnMethods);
        engine.Begin(s);
        var seen = new List<Directive>();
        for (int i = 0; i < 60 && s.Active && !s.FinishDecided; i++)
        {
            if (s.Current is { } d) seen.Add(d);
            Drive(engine, s, clock, 1);
        }
        var stroking = seen.Where(d => d.Method is not null).ToList();
        Assert.NotEmpty(stroking);
        Assert.All(stroking, d => Assert.True(d.Method!.IsUserChoice));
        Assert.Contains(seen, d => d.Speed > 0 && d.Tempo > 0);
        Assert.Null(s.PendingLube);
    }

    [Fact]
    public void FreePlay_OnlyWatches_ChecksInQuietly_AndDecidesTheEndingAtTheBudget()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.5), clock);
        var s = NewState(SessionMode.FreePlay);
        engine.Begin(s);
        Assert.Equal(StepCategory.FreePlay, s.Current!.Category);
        Assert.True(s.Current.HiddenTimer);
        Assert.Equal(0, s.Current.Tempo);
        int step = s.StepNumber;
        for (int i = 0; i < 5; i++) engine.Edge(s);
        Assert.Equal(step, s.StepNumber);
        Assert.Equal(5, s.EdgesThisSession);
        // Quiet check-ins while the user keeps going; stimulation counts while they stay active.
        while (!s.FinishDecided)
        {
            clock.Advance(SessionEngine.FreePlayCheckSeconds);
            engine.Edge(s);
            engine.Timer(s, s.TimerGeneration);
        }
        Assert.True(s.Points >= s.Budget);
        Assert.Equal(StepCategory.Finish, s.Current!.Category);
    }

    [Fact]
    public void FreePlay_StopsCountingWhenTheUserGoesQuiet()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.5), clock);
        var s = NewState(SessionMode.FreePlay);
        engine.Begin(s);
        for (int i = 0; i < 60; i++)
        {
            clock.Advance(SessionEngine.FreePlayCheckSeconds);
            engine.Timer(s, s.TimerGeneration);
        }
        // An hour of silence counts at most the unreported-stimulation allowance.
        double max = Points.ForStimulation(MethodInfo.UserChoice(s.Methods), Points.MaxUnreportedStimulation.TotalSeconds, 0);
        Assert.InRange(s.Points, 0, max + 0.001);
        Assert.False(s.FinishDecided);
    }

    [Fact]
    public void SameActivity_EarnsTheSamePoints_InEveryMode()
    {
        // With one method in the library the user's choice is that method, so points must match exactly.
        double Earned(SessionMode mode)
        {
            var clock = new FakeClock();
            var engine = new SessionEngine(new FixedRandom(0.99), clock);
            var s = NewState(mode, methods: Palm);
            engine.Begin(s);
            double before = s.Points;
            for (int i = 0; i < 4; i++) engine.Edge(s);
            return Math.Round(s.Points - before, 6);
        }
        double expected = Enumerable.Range(0, 4).Sum(i => Points.ForEdge(Palm, i));
        Assert.Equal(expected, Earned(SessionMode.FreePlay), 6);
        Assert.Equal(expected, Earned(SessionMode.OwnMethods), 6);
        Assert.Equal(expected, Earned(SessionMode.FullGuided), 6);
        Assert.Equal(expected, Earned(SessionMode.OwnContent), 6);
    }

    [Fact]
    public void Permission_UsesTheSameRules_WhoeverSetsThePace()
    {
        var guided = NewState(SessionMode.FullGuided);
        var free = NewState(SessionMode.FreePlay);
        foreach (var s in new[] { guided, free })
        {
            s.Budget = 100; s.Points = 60; s.EdgesThisSession = 6; s.RequestsThisSession = 1;
            s.StartedAtUtc = s.LastActivityUtc = new FakeClock().UtcNow;
        }
        Assert.Equal(DommePermission.Instance.Chance(guided, true), DommePermission.Instance.Chance(free, true));
        // Asking early is mostly futile; asking again annoys her.
        free.Points = 5;
        double early = DommePermission.Instance.Chance(free, true);
        free.Points = 100;
        double ready = DommePermission.Instance.Chance(free, true);
        free.RequestsThisSession = 4;
        double pestering = DommePermission.Instance.Chance(free, true);
        Assert.True(early < ready / 5);
        Assert.True(pestering < ready);
    }

    [Fact]
    public void GrantedRequest_OpensAFinishingWindow_AndAnOrgasmInsideItEndsAllowed()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.0), clock);
        var s = NewState(SessionMode.FreePlay);
        engine.Begin(s);
        var t = engine.RequestPermission(s);
        Assert.True(Assert.Single(t.Effects.OfType<PermissionAnswered>()).Granted);
        Assert.True(s.PermissionGranted);
        Assert.Equal(StepCategory.Finish, s.Current!.Category);
        Assert.NotNull(s.TimerDueUtc);
        engine.Orgasm(s, allowed: true);
        Assert.False(s.Active);
        Assert.Equal(SessionOutcome.AllowedOrgasm, s.Outcome);
    }

    [Fact]
    public void DeniedRequest_AngersHer_AndAnUnauthorizedOrgasmMarksTheOutcome()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.99), clock);
        var s = NewState(SessionMode.FullGuided);
        engine.Begin(s);
        var t = engine.RequestPermission(s);
        Assert.False(Assert.Single(t.Effects.OfType<PermissionAnswered>()).Granted);
        Assert.True(s.Retaliation.Active);
        Assert.Equal(RetaliationReason.BegCumDenied, s.Retaliation.Reason);
        engine.Orgasm(s, allowed: false);
        Assert.Equal(RetaliationReason.UnauthorizedOrgasm, s.Retaliation.Reason);
        s.Retaliation.Active = false;
        s.Points = s.Budget;
        Drive(engine, s, clock, 50);
        Assert.Equal(InstructionKind.Finish, s.Current!.Kind);
        engine.Complete(s);
        Assert.Equal(SessionOutcome.UnauthorizedOrgasm, s.Outcome);
    }

    [Fact]
    public void RequestRest_BelongsToWhoeverSetsThePace()
    {
        var engine = new SessionEngine(new FixedRandom(0.0), new FakeClock());
        var free = NewState(SessionMode.FreePlay);
        engine.Begin(free);
        Assert.NotNull(engine.RequestRest(free).Rejected);
        var guided = NewState(SessionMode.FullGuided);
        engine.Begin(guided);
        var t = engine.RequestRest(guided);
        Assert.Null(t.Rejected);
        Assert.Equal(InstructionKind.Rest, guided.Current!.Kind);
    }

    [Fact]
    public void Games_ApplyTheirStakes()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new FixedRandom(0.0), clock);
        var s = NewState(SessionMode.FullGuided, contract: SessionContract.Preset(SessionMode.FullGuided) with { Debug = true, GamesOnly = true });
        engine.Begin(s);
        Assert.NotNull(s.Game);
        // Play whatever comes up until a game ends with stakes that become a step or points.
        for (int i = 0; i < 20; i++)
        {
            var game = s.Game!;
            double points = s.Points;
            GameResult? result = null;
            Transition t;
            if (game.Input == GameInput.Choices) t = engine.Answer(s, game.Choices[^1].Value);
            else if (game.Input == GameInput.Number) t = engine.Answer(s, -1);
            else if (s.TimerDueUtc is DateTime due) { clock.UtcNow = due; t = engine.Timer(s, s.TimerGeneration); }
            else t = engine.Edge(s);
            result = t.Effects.OfType<GameFinished>().FirstOrDefault()?.Result;
            if (result is null) continue;
            foreach (var c in result.Consequences)
                if (c.Kind == ConsequenceKind.Points) Assert.Equal(Math.Max(0, points + c.Amount), s.Points, 3);
            var stepStake = result.Consequences.FirstOrDefault(c => c.Kind is ConsequenceKind.Rest or ConsequenceKind.ExtraEdges or ConsequenceKind.Torment);
            if (stepStake is not null)
            {
                Assert.Null(s.Game);
                Assert.Equal(stepStake.Kind switch
                {
                    ConsequenceKind.Rest => InstructionKind.Rest,
                    ConsequenceKind.ExtraEdges => InstructionKind.EdgeCount,
                    _ => InstructionKind.TimedMethod,
                }, s.Current!.Kind);
                if (stepStake.Kind == ConsequenceKind.ExtraEdges) Assert.Equal(stepStake.Amount, s.Current.DurationOrCount);
                return;
            }
            if (!result.Won && !result.Neutral) Assert.Equal(RetaliationReason.LostGame, s.Retaliation.Reason);
        }
        Assert.Fail("No game applied a step stake.");
    }

    [Fact]
    public void EdgeGames_AreJudgedFromTheTracker_NotFromReportedCounts()
    {
        var clock = new FakeClock();
        var random = new FixedRandom(0.0);
        var engine = new SessionEngine(random, clock);
        var s = NewState(SessionMode.FullGuided);
        engine.Begin(s);
        var planner = new RulePlanner(random, Authorities.For(s.Contract, random));
        var (game, first) = Games.Start(GameType.EdgeRush, planner.GameContext(s, Palm, clock.UtcNow));
        s.Game = game;
        s.Current = first.Next;
        s.EdgesThisStep = 0;
        Assert.Equal(GameInput.None, game.Input);
        for (int i = 0; i < game.Target - 1; i++) engine.Edge(s);
        Assert.NotNull(s.Game);
        var t = engine.Edge(s);
        Assert.True(Assert.Single(t.Effects.OfType<GameFinished>()).Result!.Won);
    }

    [Fact]
    public void Chicken_PaysForTimeSurvived_AndAnEdgeBeforeBailingLoses()
    {
        var clock = new FakeClock();
        var random = new FixedRandom(0.0);
        var s = NewState(SessionMode.FullGuided);
        var planner = new RulePlanner(random, Authorities.For(s.Contract, random));
        var (game, _) = Games.Start(GameType.Chicken, planner.GameContext(s, Palm, clock.UtcNow));
        clock.Advance(35);
        var bailed = Games.OnAnswer(game, 1, planner.GameContext(s, Palm, clock.UtcNow))!.Result!;
        Assert.True(bailed.Won);
        Assert.Equal(6, Assert.Single(bailed.Consequences).Amount);
        var (again, _) = Games.Start(GameType.Chicken, planner.GameContext(s, Palm, clock.UtcNow));
        var edged = Games.OnEdge(again, planner.GameContext(s, Palm, clock.UtcNow), 1)!.Result!;
        Assert.False(edged.Won);
    }

    [Fact]
    public void RetiredGames_AreNeverChosen()
    {
        var random = new SeededRandom(5);
        var s = NewState(SessionMode.FullGuided);
        s.Arousal = 90;
        var planner = new RulePlanner(random, Authorities.For(s.Contract, random));
        for (int i = 0; i < 500; i++)
        {
            var type = Games.Choose(planner.GameContext(s, Palm, DateTime.UtcNow), [], anyGame: i % 2 == 0);
            Assert.DoesNotContain(type, new[] { GameType.EdgeCounter, GameType.MemorySequence });
        }
    }

    [Fact]
    public void EdgeDuringATimedStep_HoldsOrCoolsDown_ThenResumesTheRemainder()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new SeededRandom(21), clock);
        var s = NewState(SessionMode.FullGuided);
        engine.Begin(s);
        for (int i = 0; i < 40 && s.Current!.Kind != InstructionKind.TimedMethod; i++) Drive(engine, s, clock, 1);
        Assert.Equal(InstructionKind.TimedMethod, s.Current!.Kind);
        var timed = s.Current;
        int step = s.StepNumber;
        clock.Advance(3);
        engine.Edge(s);
        Assert.Equal(step, s.StepNumber);
        Assert.True(s.Current!.Category is StepCategory.Hold or StepCategory.Rest);
        if (s.Game is null && s.Suspended is not null)
        {
            clock.UtcNow = s.TimerDueUtc!.Value;
            engine.Timer(s, s.TimerGeneration);
            Assert.Equal(timed.Method, s.Current!.Method);
            Assert.True(s.Current.DurationOrCount < timed.DurationOrCount);
        }
    }

    [Fact]
    public void PauseFreezesTheCountdown_AndStaleTimersAreIgnored()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new SeededRandom(9), clock);
        var s = NewState(SessionMode.FullGuided);
        engine.Begin(s);
        for (int i = 0; i < 40 && s.TimerDueUtc is null; i++) Drive(engine, s, clock, 1);
        var due = s.TimerDueUtc!.Value;
        var generation = s.TimerGeneration;
        clock.Advance(2);
        int remaining = (int)Math.Ceiling((due - clock.UtcNow).TotalSeconds);
        engine.Pause(s);
        Assert.Equal(remaining, s.PausedTimerSeconds);
        clock.Advance(600);
        Assert.Empty(engine.Timer(s, generation).Domme);
        engine.Resume(s);
        Assert.Equal(clock.UtcNow.AddSeconds(remaining), s.TimerDueUtc);
        Assert.NotEqual(generation, s.TimerGeneration);
    }

    [Fact]
    public void StateRoundTripsThroughTheCheckpoint()
    {
        var clock = new FakeClock();
        var engine = new SessionEngine(new SeededRandom(4), clock);
        var s = NewState(SessionMode.OwnMethods);
        engine.Begin(s);
        Drive(engine, s, clock, 15);
        var json = JsonSerializer.Serialize(s, GuidedRecovery.Json);
        var restored = JsonSerializer.Deserialize<SessionState>(json, GuidedRecovery.Json)!;
        Assert.Equal(json, JsonSerializer.Serialize(restored, GuidedRecovery.Json));
        Assert.Equal(JsonSerializer.Serialize(s.Contract, GuidedRecovery.Json), JsonSerializer.Serialize(restored.Contract, GuidedRecovery.Json));
    }

    [Fact]
    public void ContractChange_IsRefusedWhenLocked_AndReplacesTheStepWhenAllowed()
    {
        var engine = new SessionEngine(new SeededRandom(2), new FakeClock());
        var locked = NewState(SessionMode.FullGuided);
        engine.Begin(locked);
        Assert.Throws<InvalidOperationException>(() => engine.ChangeContract(locked, SessionContract.Preset(SessionMode.FreePlay), [Palm]));
        var open = NewState(SessionMode.FullGuided, contract: SessionContract.Preset(SessionMode.FullGuided) with { CanChangeDuringSession = true });
        engine.Begin(open);
        engine.Edge(open);
        int edges = open.EdgesThisSession;
        engine.ChangeContract(open, (SessionContract.Preset(SessionMode.FreePlay) with { CanChangeDuringSession = true }).Validate(), [Palm, Grip]);
        Assert.Equal(SessionMode.FreePlay, open.Contract.Mode);
        Assert.Equal(StepCategory.FreePlay, open.Current!.Category);
        Assert.Equal(edges, open.EdgesThisSession);
        Assert.Equal(2, open.ContractRevision);
    }
}
