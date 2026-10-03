using AIDome.Sessions;
using AIDome.Sessions.Plan;
using AIDome.Speech;
using Xunit;

namespace AIDome.Tests;

/// <summary>
/// Phase 5: the model of his body, what she learns from his edges, his profile, and how she
/// chooses aims and segments from his state.
/// </summary>
public sealed class BodyTests
{
    private static readonly MethodInfo Strong = new() { Id = 1, Name = "Strong", Intensity = 7, Closeness = 8, TimePoints = 0.08, EdgePoints = 3 };
    private static readonly MethodInfo Mild = Strong with { Id = 2, Name = "Mild", Intensity = 2 };
    private static readonly MethodInfo Tease = new() { Id = 3, Name = "Tease", Intensity = 1, Tease = 9, Closeness = 2, TimePoints = 0.03, EdgePoints = 2 };
    private static readonly MethodInfo Torture = new() { Id = 4, Name = "Torture", Intensity = 9, TortLevel = 9, Closeness = 1, TimePoints = 0.1, EdgePoints = 3 };

    /// <summary>Strokes until the true body reaches the edge, moving her estimate alongside; returns the seconds it took.</summary>
    private static int Edge(BodyState truth, ProfileState actual, BodyState estimate, ProfileState believed, MethodInfo method, int speed)
    {
        int seconds = 0;
        while (truth.Closeness < 0.999 && seconds < 900)
        {
            Body.Advance(truth, actual, BodyMode.Stroke, method, speed, 1);
            Body.Advance(estimate, believed, BodyMode.Stroke, method, speed, 1, Body.EstimateCap);
            seconds++;
        }
        Body.OnEdge(truth, actual, learn: false, 0);
        Body.OnEdge(estimate, believed, learn: true, 0, estimate: true);
        return seconds;
    }

    private static void Rest(BodyState truth, ProfileState actual, BodyState estimate, ProfileState believed, int seconds)
    {
        Body.Advance(truth, actual, BodyMode.Rest, null, 0, seconds);
        Body.Advance(estimate, believed, BodyMode.Rest, null, 0, seconds, Body.EstimateCap);
    }

    [Theory]
    [InlineData(0.6, 40)]
    [InlineData(1.3, 18)]
    public void AFewEdges_TeachHerHisPaceAndRecovery(double pace, double recovery)
    {
        var actual = new ProfileState { Pace = pace, Recovery = recovery };
        var believed = new ProfileState();
        var truth = new BodyState();
        var estimate = new BodyState();
        for (int i = 0; i < 8; i++)
        {
            Edge(truth, actual, estimate, believed, Strong, 6);
            Rest(truth, actual, estimate, believed, 25);
        }
        Assert.InRange(believed.Pace, pace * 0.8, pace * 1.2);
        Assert.InRange(believed.Recovery, recovery * 0.65, recovery * 1.35);
        Assert.StartsWith("learned from", believed.PaceSource);
        // Her expectation of the later edges is close to what happened.
        var late = estimate.Checks.Skip(4).ToList();
        Assert.True(late.Average(c => Math.Abs(c.ActualSeconds - c.ExpectedSeconds) / c.ExpectedSeconds) < 0.3);
    }

    [Fact]
    public void AnEdge_CorrectsHerEstimateToTheEdge()
    {
        var p = new ProfileState();
        var b = new BodyState();
        Body.Advance(b, p, BodyMode.Stroke, Strong, 6, 20, Body.EstimateCap);
        Assert.InRange(b.Closeness, 0.05, 0.9);
        Body.OnEdge(b, p, learn: true, 20, estimate: true);
        Assert.Equal(1, b.Closeness);
    }

    [Fact]
    public void AWeakMethodAtACrawl_LevelsOffWellShortOfTheEdge()
    {
        var p = new ProfileState();
        var b = new BodyState();
        Body.Advance(b, p, BodyMode.Stroke, Tease, 1, 900);
        Assert.InRange(b.Closeness, 0.05, 0.6);
        Assert.Equal(Body.Unreachable, Body.ExpectedSeconds(p, Tease, 1, 0, 0));
        // The same seconds on a strong method at a good pace reach it.
        Assert.InRange(Body.ExpectedSeconds(p, Strong, 7, 0, 0), 20, 150);
    }

    [Fact]
    public void RestBringsHimDown_ByHisRecovery()
    {
        var p = new ProfileState { Recovery = 30 };
        var b = new BodyState { Closeness = 1 };
        Body.Advance(b, p, BodyMode.Rest, null, 0, 30);
        Assert.InRange(b.Closeness, 0.48, 0.52);
        Assert.InRange(Body.RestFor(p, 0, 1, 0.5), 29, 31);
        // Need slows it.
        Assert.True(Body.RestFor(p, 0.8, 1, 0.5) > 40);
    }

    [Fact]
    public void IntensityWearsControl_AndTortureBuildsOverwhelm_Separately()
    {
        var p = new ProfileState { Tolerance = 45 };
        Assert.True(Body.Tolerance(p, Strong) < Body.Tolerance(p, Mild));
        // A hold spends his control at the rate his tolerance on that method allows.
        var held = new BodyState { Closeness = 1 };
        Body.Advance(held, p, BodyMode.Hold, Mild, 0, 20);
        var heldHard = new BodyState { Closeness = 1 };
        Body.Advance(heldHard, p, BodyMode.Hold, Strong, 0, 20);
        Assert.True(heldHard.Control < held.Control);
        Assert.Equal(0, held.Overwhelm);
        // Torture overwhelms without bringing him close or touching his control.
        var tormented = new BodyState();
        Body.Advance(tormented, p, BodyMode.Stroke, Torture, 8, 120);
        Assert.True(tormented.Overwhelm > 0.5);
        Assert.True(tormented.Closeness < 0.6);
        Assert.Equal(1, tormented.Control);
        // And a strong, pleasant method does not overwhelm.
        var stroked = new BodyState();
        Body.Advance(stroked, p, BodyMode.Stroke, Strong, 8, 120, Body.TimedCap);
        Assert.Equal(0, stroked.Overwhelm);
    }

    [Fact]
    public void TheGapIsHerLever_ControlComesBackOnlyWhenHeIsLetDown()
    {
        var p = new ProfileState();
        // Kept high: twenty seconds give him almost nothing back.
        Assert.True(Body.Regain(0.8) * 20 < 0.05);
        // Well down: the same twenty seconds give back a good part of it.
        Assert.True(Body.Regain(0.3) * 20 > 0.3);
        Assert.True(Body.RestToRegain(p, 0, 1, 0.1, 0.5) is > 15 and < 90);
    }

    [Fact]
    public void Tolerance_MovesSlowlyFromWhatHeTakes()
    {
        var p = ProfileState.From(new ProfileSettings { Tolerance = ToleranceLevel.AWhile }, new LearnedProfile());
        double set = p.Tolerance;
        // He went over when she thought he had plenty left: a little down.
        Body.OnSlip(new BodyState { Control = 0.8 }, p);
        Assert.InRange(p.Tolerance, set * 0.9, set * 0.99);
        // A slip she saw coming says nothing new.
        double after = p.Tolerance;
        Body.OnSlip(new BodyState { Control = 0.1 }, p);
        Assert.Equal(after, p.Tolerance);
        // Getting through what she reckoned was his limit moves it up, a little, and never past twice his setting.
        for (int i = 0; i < 200; i++) p.Survived();
        Assert.Equal(set * ProfileState.ToleranceUp, p.Tolerance, 3);
        Assert.Contains("moved up", p.ToleranceSource);
    }

    [Fact]
    public void HisProfile_ComesFromHisSettingAndWhatSheLearned()
    {
        Assert.True(ProfileState.ToleranceFor(ToleranceLevel.Briefly) < ProfileState.ToleranceFor(ToleranceLevel.AWhile));
        Assert.True(ProfileState.ToleranceFor(ToleranceLevel.AWhile) < ProfileState.ToleranceFor(ToleranceLevel.ALongTime));

        var learned = new LearnedProfile { Pace = 0.8, PaceEvidence = 12, Recovery = 33, RecoveryEvidence = 9, ToleranceFactor = 1.2 };
        var p = ProfileState.From(new ProfileSettings { Tolerance = ToleranceLevel.Briefly }, learned);
        Assert.Equal(0.8, p.Pace);
        Assert.Equal(33, p.Recovery);
        Assert.Equal(ProfileState.ToleranceFor(ToleranceLevel.Briefly) * 1.2, p.Tolerance, 3);
        Assert.StartsWith("his setting: briefly", p.ToleranceSource);
        // What earlier sessions taught her still lets tonight move it.
        Assert.True(p.PaceEvidence <= 6);

        // A value set in the lab is fixed: learning leaves it alone, and it is not carried into what she has learned.
        var fixedPace = ProfileState.From(new ProfileSettings { Pace = 1.5, ToleranceSeconds = 80 }, learned);
        Assert.Equal(1.5, fixedPace.Pace);
        Assert.Equal("set in the lab", fixedPace.PaceSource);
        Assert.Equal(80 * 1.2, fixedPace.Tolerance, 3);
        fixedPace.Recovery = 50;
        var kept = LearnedProfile.From(fixedPace, learned);
        Assert.Equal(0.8, kept.Pace);
        Assert.Equal(50, kept.Recovery);

        Assert.Throws<ArgumentException>(() => new ProfileSettings { Pace = 9 }.Validate());
        Assert.Throws<ArgumentException>(() => new ProfileSettings { ToleranceSeconds = 1 }.Validate());
        Assert.Equal(ToleranceLevel.AWhile, new DommeSettings().Validate().Profile.Tolerance);
    }

    [Fact]
    public void MethodsAreChosenForWhatTheyMakeHimFeel()
    {
        var s = TranscriptTests.NewSession(SessionMode.FullGuided, 3);
        s.Methods = TranscriptTests.Library.ToList();
        var r = new SeededRandom(4);
        for (int i = 0; i < 30; i++)
        {
            var control = MethodFit.Choose(s, Purpose.Control, 4, r);
            Assert.InRange(control.Count, 1, 4);
            Assert.All(control, m => Assert.True(m.Closeness >= MethodPreference.EdgeReach, m.Name));
            var overwhelm = MethodFit.Choose(s, Purpose.Overwhelm, 3, r);
            Assert.All(overwhelm, m => Assert.True(m.TortLevel >= 5, m.Name));
            var ache = MethodFit.Choose(s, Purpose.Ache, 3, r);
            Assert.All(ache, m => Assert.True(m.TortLevel < 5 && m.Closeness >= MethodPreference.EdgeReach, m.Name));
            var starve = MethodFit.Choose(s, Purpose.Starve, 2, r);
            Assert.All(starve, m => Assert.True(m.Tease >= 4, m.Name));
            // One segment does not mix lubed and dry.
            Assert.True(control.Select(m => m.RequiresLube).Distinct().Count() == 1);
        }
    }

    [Fact]
    public void HerAims_ComeFromHisStateAndWhatSheControls()
    {
        var catalogue = SegmentCatalogue.Default;
        int firstAche = 0;
        for (int seed = 0; seed < 40; seed++)
        {
            var r = new SeededRandom(seed);
            var s = TranscriptTests.NewSession(SessionMode.FullGuided, seed, length: 7);
            s.Budget = Points.Budget(s.Length, false, s.Lengths, s.Recall);
            var a = Authorities.For(s.Contract, r);
            var engine = new PlanEngine(r, catalogue);
            var plan = engine.Make(s, a);
            Assert.InRange(plan.Aims.Count, 2, 8);
            Assert.Equal(1, plan.Aims.Sum(x => x.Share), 2);
            Assert.All(plan.Aims, aim => { Assert.NotEmpty(aim.Why); Assert.NotEmpty(aim.Goal); });
            if (plan.Aims[0].Purpose == Purpose.Ache) firstAche++;

            // With his need low she builds the ache; with it high and his control intact she tests it.
            var low = engine.Weigh(s, a, plan, new BodyState { Need = 0.05 }, 0.3, null);
            var high = engine.Weigh(s, a, plan, new BodyState { Need = 0.8 }, 0.5, null);
            double W(List<(Purpose Purpose, double Weight, string Why)> list, Purpose p) => list.First(x => x.Purpose == p).Weight;
            Assert.True(W(low, Purpose.Ache) > W(low, Purpose.Control));
            Assert.True(W(high, Purpose.Control) > W(high, Purpose.Ache));
            // Thin control makes testing it less attractive; being overwhelmed already makes more of it less so.
            Assert.True(W(engine.Weigh(s, a, plan, new BodyState { Need = 0.8, Control = 0.1 }, 0.5, null), Purpose.Control) < W(high, Purpose.Control));
            Assert.True(W(engine.Weigh(s, a, plan, new BodyState { Need = 0.5, Overwhelm = 0.9 }, 0.5, null), Purpose.Overwhelm)
                < W(engine.Weigh(s, a, plan, new BodyState { Need = 0.5 }, 0.5, null), Purpose.Overwhelm));

            // When the edges are his, she cannot test his control with them.
            var his = TranscriptTests.NewSession(SessionMode.Custom, seed, length: 7);
            his.Budget = Points.Budget(his.Length, false, his.Lengths, his.Recall);
            var hisPlan = new PlanEngine(r, catalogue).Make(his, Authorities.For(his.Contract, r));
            Assert.DoesNotContain(hisPlan.Aims, x => x.Purpose == Purpose.Control);

            // A punishment session includes overwhelm.
            var punishment = TranscriptTests.NewSession(SessionMode.FullGuided, seed, punishment: true, length: 5);
            punishment.Budget = Points.Budget(punishment.Length, true, punishment.Lengths, punishment.Recall);
            var punishing = new PlanEngine(r, catalogue).Make(punishment, Authorities.For(punishment.Contract, r));
            Assert.Contains(punishing.Aims, x => x.Purpose == Purpose.Overwhelm);
        }
        Assert.True(firstAche >= 36, $"She opened with the ache in {firstAche} of 40 sessions.");
    }

    [Fact]
    public void SheSpendsHisControlAndStopsShortOfASlip_WhenHisSettingIsRight()
    {
        // He told her the truth about his tolerance: across sessions she wears his control down without pushing him over.
        int slips = 0, tested = 0;
        for (int seed = 1; seed <= 12; seed++)
        {
            var s = TranscriptTests.NewSession(SessionMode.FullGuided, 300 + seed, length: 6);
            var user = SimulatedUser.Obedient with { Pace = 0.9, Recovery = 28, Tolerance = 45, Offers = "decline" };
            var result = Simulation.Run(s, user, seed, chatter: false);
            if (Environment.GetEnvironmentVariable("AIDOMME_TRANSCRIPTS") is { Length: > 0 } folder)
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(folder).FullName, $"Truthful-tolerance-{seed}.md"), Simulation.Transcript(result, $"Truthful-tolerance-{seed}"));
            if (result.State.UnauthorizedOrgasms > 0) slips++;
            if (result.State.Body.LowestControl < 0.6) tested++;
            // Every segment she chose says why and with what parameters.
            Assert.All(result.State.Plan!.Segments, x => Assert.NotEmpty(x.Reason));
        }
        Assert.True(slips <= 1, $"{slips} of 12 sessions ended in a slip.");
        Assert.True(tested >= 6, $"His control was tested in only {tested} of 12 sessions.");
    }

    [Fact]
    public void ASimulatedSession_ReplaysExactlyFromItsSeed()
    {
        string Run() => Simulation.Transcript(Simulation.Run(TranscriptTests.NewSession(SessionMode.FullGuided, 21), SimulatedUser.Beggar, 21), "replay");
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void TheTranscript_ShowsWhatIsNeededToDebugASession()
    {
        var result = Simulation.Run(TranscriptTests.NewSession(SessionMode.FullGuided, 33, length: 5), SimulatedUser.Obedient, 33);
        string text = Simulation.Transcript(result, "debug");
        foreach (var expected in new[]
        {
            "Close (est)", "Close (true)", "| Need | Control (est) | Control (true) | Overwhelm |", "Gap",
            "## His state", "Time by closeness (her estimate)", "Time by closeness (true)", "Session profile", "**Points:**",
            "His profile as she has it at the end", "| Pace", "| Recovery", "| Tolerance", "Estimator check", "Average miss",
            "Her aims", "## Aims and segments", "Why she chose it", "Why it ended", "| Parameters |", "improvised", "Time by purpose", "seed 33",
        })
            Assert.Contains(expected, text);
        // The points are all accounted for.
        var e = result.State.Earned;
        Assert.True(e.Stroking + e.Torture + e.Edges > 0);
        // Step records carry her estimate and the truth.
        Assert.Contains(result.Steps, x => x.EstimateEnd.Closeness > 0.5 && x.TruthEnd.Closeness > 0.5);
    }

    [Fact]
    public void TheSameSecondsEarnTheSame_WhoeverSetsThePace()
    {
        // His state changes what stimulation is worth, by her estimate of his closeness and the method only.
        Assert.True(Points.StateFactor(0.9, Strong) > Points.StateFactor(0.2, Strong));
        Assert.True(Points.StateFactor(0.9, Strong) > Points.StateFactor(0.9, Mild));
        Assert.InRange(Points.StateFactor(0.8, Strong with { Intensity = 5 }), 0.95, 1.08);
        Assert.Equal(Body.TimedCap, Body.SelfPacedCap);
    }

    [Fact]
    public void ALengthMeansItsMinutes_WhateverHisPace()
    {
        // Early on she goes by what she assumed; as the minutes add up, by what he is earning.
        Assert.Equal(5.5, Points.PaceTonight(5.5, 30, 1));
        Assert.InRange(Points.PaceTonight(5.5, 8 * 9.0, 8), 7.2, 7.3);
        Assert.InRange(Points.PaceTonight(5.5, 40 * 9.0, 40), 8.2, 8.4);
        // One wild stretch cannot run away with it.
        Assert.True(Points.PaceTonight(5.5, 40 * 100.0, 40) <= 5.5 * 2.5);

        foreach (int length in new[] { 3, 5, 7 })
        {
            var s = TranscriptTests.NewSession(SessionMode.FullGuided, 60 + length, length: length);
            var result = Simulation.Run(s, SimulatedUser.Obedient with { Offers = "decline" }, 60 + length, chatter: false);
            double minutes = (result.Steps[^1].End - result.Steps[0].Start).TotalMinutes;
            Assert.InRange(minutes, s.BudgetMinutes * 0.7, s.BudgetMinutes * 1.3);
        }
    }

    [Fact]
    public void AHiddenCount_IsEdgeThenAgain()
    {
        var r = new SeededRandom(1);
        var d = new Directive { Kind = Data.InstructionKind.EdgeCount, Method = Strong with { Description = "Stroke." }, Speed = 6, DurationOrCount = 1 };
        var again = new Utterance(SpeechAct.Instruct) { Directive = d, Tags = ["again-same"] };
        Assert.Equal("Again.", Grammar.Instruction(again, r));
        var changed = new Utterance(SpeechAct.Instruct) { Directive = d, Tags = ["again"] };
        Assert.EndsWith("and edge again.", Grammar.Instruction(changed, r));
        var counted = new Utterance(SpeechAct.Instruct) { Directive = d, Tags = ["again-same", "left:2"] };
        Assert.Equal("Again. Two to go.", Grammar.Instruction(counted, r));
    }
}
