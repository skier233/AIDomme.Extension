using System.Text.Json;
using AIDome.Persistence;
using AIDome.Sessions;
using AIDome.Sessions.Plan;
using AIDome.Speech;
using Xunit;
using Xunit.Abstractions;

namespace AIDome.Tests;

/// <summary>
/// Whole simulated sessions across modes, simulated users and seeds: the invariants her sessions
/// and speech must keep, and (when AIDOMME_TRANSCRIPTS names a folder) readable transcripts.
/// </summary>
public sealed class TranscriptTests(ITestOutputHelper output)
{
    private static MethodInfo Method(int id, string name, string description, int intensity, int tease, int torture, int closeness, bool lube = false) => new()
    {
        Id = id, Name = name, Description = description, Intensity = intensity, Tease = tease, TortLevel = torture, Closeness = closeness,
        RequiresLube = lube, TimePoints = 0.04 + intensity * 0.005, EdgePoints = 2 + closeness * 0.1,
    };

    /// <summary>A realistic slice of the shipped method library.</summary>
    public static readonly List<MethodInfo> Library =
    [
        Method(1, "Figure Eight", "Perform the figure eight on your cock.", 6, 0, 4, 7, lube: true),
        Method(2, "Stroke From Under (dry)", "Stroke your cock from under your leg.", 4, 0, 1, 3),
        Method(4, "Palm", "Palm your cock.", 9, 0, 9, 1, lube: true),
        Method(5, "Worse Than Palm", "Perform worse than palming on your cock.", 10, 0, 10, 2, lube: true),
        Method(7, "Roll Between Hands", "Roll your cock between your hands.", 3, 3, 0, 3),
        Method(8, "Stomach Rub", "Rub your cock back and forth across your stomach.", 4, 7, 0, 3, lube: true),
        Method(10, "1-Hand Stroke (dry)", "Stroke up and down your cock with 1 hand.", 4, 0, 1, 3),
        Method(11, "1-Hand Stroke (lube)", "Stroke up and down your cock with 1 hand.", 6, 0, 3, 5, lube: true),
        Method(12, "2-Hand Stroke", "Stroke up and down your cock with 2 hands.", 7, 0, 2, 8, lube: true),
        Method(13, "Head Stroke", "Stroke up and down the head of your cock.", 7, 2, 5, 6, lube: true),
        Method(20, "Head Twist", "Twist the head of your cock.", 8, 1, 7, 4, lube: true),
        Method(28, "Fist Peek", "Make a fist and peek the head of your cock into it.", 6, 5, 4, 4, lube: true),
        Method(30, "Single Finger Tease", "Move one finger up and down your cock gently.", 1, 9, 1, 1),
    ];

    /// <summary>The shipped offer library (migration 007).</summary>
    public static readonly List<OfferTemplate> Offers =
    [
        new() { Id = 1, Name = "Earn it", Wording = "Earn it. Edge [amount] more time[s] for me, and then you may [what].", Kind = OfferKind.EdgesNow, MinAmount = 3, MaxAmount = 10, PointsPerUnit = 4, Flavor = OfferFlavor.Strict, Moods = [DommeMood.Edgy, DommeMood.Relentless], ForOrgasm = true },
        new() { Id = 3, Name = "Pay in days", Wording = "You may [what]. Then you're locked away for [amount] day[s]. No exceptions.", Kind = OfferKind.Denial, MinAmount = 1, MaxAmount = 7, PointsPerUnit = 14, Flavor = OfferFlavor.Strict, ForOrgasm = true, ForMercy = true },
        new() { Id = 4, Name = "Owe me a session", Wording = "Fine, [what]. But you'll owe me [amount] punishment session[s], and I won't be gentle.", Kind = OfferKind.PunishmentSession, MinAmount = 1, MaxAmount = 2, PointsPerUnit = 60, Flavor = OfferFlavor.Cruel, Moods = [DommeMood.Cruel], ForOrgasm = true, ForMercy = true },
        new() { Id = 6, Name = "Ruined, then", Wording = "You can cum. I'm just not letting you enjoy it: you're ruining it.", Kind = OfferKind.RuinNow, MinAmount = 1, MaxAmount = 1, PointsPerUnit = 30, Flavor = OfferFlavor.Teasing, ForOrgasm = true },
        new() { Id = 8, Name = "Edges on account", Wording = "Okay, [what]. But you owe me [amount] edge[s] before you cum again. I'm keeping count.", Kind = OfferKind.EdgesLater, MinAmount = 3, MaxAmount = 15, PointsPerUnit = 4, Flavor = OfferFlavor.Playful, ForRest = true, ForMercy = true },
        new() { Id = 9, Name = "Buy a break", Wording = "Want a break? Edge [amount] more time[s] first.", Kind = OfferKind.EdgesNow, MinAmount = 2, MaxAmount = 4, PointsPerUnit = 4, Flavor = OfferFlavor.Playful, ForRest = true },
    ];

    private static readonly Screen OnScreen = new()
    {
        Kind = "video", Id = 42, Title = "Evening", Performers = ["Mia"], Tags = ["Handjob", "POV"], NowTags = ["Teasing"],
    };

    public static SessionState NewSession(SessionMode mode, int seed, bool punishment = false, PersonaSettings? persona = null, Recall? recall = null,
        SessionObligations? owed = null, int length = 4)
    {
        recall ??= new();
        var contract = (mode == SessionMode.Custom
            ? SessionContract.Preset(SessionMode.FullGuided) with { Edges = Authority.User, Games = true }
            : SessionContract.Preset(mode)) with
        {
            Preferences = new() { Intensity = 5, Length = length, Denial = 5, Mean = 5 },
            Punishment = punishment,
            Persona = SessionPersona.For(persona ?? new(), recall, new SeededRandom(seed + 1000)),
            Obligations = owed ?? new(),
        };
        return new()
        {
            SessionId = seed, Contract = contract.Validate(), Methods = Library.ToList(), OfferTemplates = Offers.ToList(), Recall = recall,
            Plan = new SessionPlan(),
        };
    }

    private static readonly SessionMode[] Modes = [SessionMode.FullGuided, SessionMode.OwnMethods, SessionMode.FreePlay, SessionMode.Custom];

    public static IEnumerable<(string Name, SimulationResult Result)> Runs()
    {
        foreach (var mode in Modes)
            foreach (var user in SimulatedUser.All)
                for (int seed = 1; seed <= 3; seed++)
                {
                    var s = NewSession(mode, seed * 31 + (int)mode);
                    yield return ($"{mode}-{user.Name.Replace(' ', '-')}-{seed}", Simulation.Run(s, user, seed, screen: seed == 2 ? OnScreen : null));
                }
        for (int seed = 1; seed <= 2; seed++)
            yield return ($"Punishment-obedient-{seed}", Simulation.Run(NewSession(SessionMode.FullGuided, seed + 500, punishment: true), SimulatedUser.Obedient, seed));
        var persona = new PersonaSettings { Harshness = 9, Humiliation = 8, Manipulation = 8, Talkativeness = 8 };
        yield return ("Harsh-talkative-obedient", Simulation.Run(NewSession(SessionMode.FullGuided, 77, persona: persona), SimulatedUser.Obedient, 7, screen: OnScreen));
        var gentle = new PersonaSettings { Harshness = 2, Humiliation = 0, Manipulation = 2, Talkativeness = 3 };
        yield return ("Gentle-quiet-obedient", Simulation.Run(NewSession(SessionMode.FullGuided, 78, persona: gentle), SimulatedUser.Obedient, 8));
        var owed = new SessionObligations { NoOrgasmUntilUtc = DateTime.MaxValue, NoOrgasmReason = "Promise", OwedEdges = 6, PunishmentEnding = false };
        yield return ("Promised-denial-with-debt", Simulation.Run(NewSession(SessionMode.FullGuided, 79, owed: owed), SimulatedUser.Beggar, 9));
    }

    [Fact]
    public void TheShippedLinesAndSegmentsAreValid()
    {
        Assert.Empty(LineLibrary.Default.Validate());
        Assert.Empty(SegmentCatalogue.Default.Validate());
    }

    [Fact]
    public void SimulatedSessions_KeepHerInvariants_AndCoverTheCatalogue()
    {
        var folder = Environment.GetEnvironmentVariable("AIDOMME_TRANSCRIPTS");
        if (!string.IsNullOrWhiteSpace(folder)) Directory.CreateDirectory(folder);
        var segments = new HashSet<string>();
        var outcomes = new HashSet<string>();
        int stories = 0, onScreen = 0, runs = 0;
        foreach (var (name, result) in Runs())
        {
            runs++;
            var s = result.State;
            if (!string.IsNullOrWhiteSpace(folder)) File.WriteAllText(Path.Combine(folder, $"{name}.md"), Simulation.Transcript(result, name));
            string context = $"{name}: {string.Join(" / ", result.Lines.TakeLast(6).Select(x => $"{x.Who}: {x.Text}"))}";

            Assert.False(s.Active, $"The session did not end. {context}");
            Assert.NotEqual(Data.SessionOutcome.Unknown, s.Outcome);
            outcomes.Add(s.Outcome.ToString());
            foreach (var seg in s.Plan?.Segments ?? []) if (seg.Started) segments.Add(seg.Type);

            var hers = result.Lines.Where(x => x.Who.StartsWith("Her")).ToList();
            foreach (var line in hers)
            {
                Assert.False(string.IsNullOrWhiteSpace(line.Text), context);
                Assert.DoesNotContain("[", line.Text);
                Assert.DoesNotContain("  ", line.Text);
                Assert.DoesNotContain(" .", line.Text);
                // Points are her clock, never something she talks about; torment overwhelms, it doesn't hurt.
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(line.Text, @"points", System.Text.RegularExpressions.RegexOptions.IgnoreCase), $"She mentioned points in {name}: {line.Text}");
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(line.Text, @"hurts?", System.Text.RegularExpressions.RegexOptions.IgnoreCase), $"She called it pain in {name}: {line.Text}");
                // She only asks for edges on a method that can get him there.
                // Edging the hard way is deliberate: an intense grip that barely gets him there, never one that can't.
                if (line.Directive is { Method: { IsUserChoice: false } m } d && (d.Kind == Data.InstructionKind.EdgeCount || d.CountEdges))
                {
                    bool hard = d.Segment is { } seg && SegmentCatalogue.Default.Find(seg)?.Step.Reach == "hard";
                    Assert.True(hard ? m.Closeness is >= MethodPreference.HardReachMin and <= MethodPreference.HardReachMax : m.Closeness >= MethodPreference.EdgeReach,
                        $"Edges asked on {m.Name} (closeness {m.Closeness}) in {name}: {line.Text}");
                }
            }
            // Asides never repeat within a session; a story is told once.
            var asides = hers.Where(x => x.Who == "Her (aside)").Select(x => x.Text).ToList();
            Assert.True(asides.Count == asides.Distinct().Count(), $"An aside repeated. {name}: {string.Join(" | ", asides.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key))}");
            stories += hers.Count(x => x.Module?.StartsWith("story.") == true && x.Text == LineLibrary.Default.Modules.First(m => m.Id == x.Module).Lines[0]);
            onScreen += hers.Count(x => x.Text.Contains("Mia"));
            // She gives one order at a time.
            foreach (var turn in hers.Where(x => x.Directive is not null).GroupBy(x => x.Turn))
                Assert.True(turn.Count() == 1, $"Two orders at once in {name}: {string.Join(" | ", turn.Select(x => x.Text))}");
            // No two identical lines back to back.
            for (int i = 1; i < hers.Count; i++)
                Assert.False(hers[i].Text == hers[i - 1].Text && hers[i].Turn == hers[i - 1].Turn, $"Repeated line in {name}: {hers[i].Text}");
            if (s.Contract.Mode == SessionMode.FreePlay) Assert.Null(s.Plan);
        }
        var planned = SegmentCatalogue.Default.Types.Where(x => x.Planned).Select(x => x.Id).ToList();
        var missing = planned.Except(segments).ToList();
        output.WriteLine($"{runs} sessions; segments never used: {string.Join(", ", missing)}; outcomes: {string.Join(", ", outcomes)}; stories told: {stories}; lines about the screen: {onScreen}");
        Assert.True(missing.Count <= planned.Count / 5, $"Segments never planned: {string.Join(", ", missing)}");
        Assert.Contains("AllowedOrgasm", outcomes);
        Assert.Contains("Denied", outcomes);
        Assert.True(stories > 0, "She never told a story.");
        Assert.True(onScreen > 0, "She never mentioned what was on screen.");
    }

    [Fact]
    public void StoriesStayRare_AcrossAWeekOfSessions()
    {
        var told = new List<(int Session, string Module)>();
        var usage = new Dictionary<string, ModuleUse>(StringComparer.OrdinalIgnoreCase);
        var start = new DateTime(2026, 9, 23, 21, 0, 0, DateTimeKind.Utc);
        var persona = new PersonaSettings { Talkativeness = 9, Manipulation = 8, Humiliation = 8, Harshness = 6 };
        for (int session = 0; session < 10; session++)
        {
            // Each session is a day later; the simulated clock always starts at the same moment, so age the uses instead.
            var aged = usage.ToDictionary(x => x.Key, x => x.Value with { LastUsedUtc = start.AddDays(-(session - x.Value.SessionsAgo)), SessionsAgo = session - x.Value.SessionsAgo });
            var recall = new Recall { Sessions = session, Usage = aged, EdgesSinceOrgasm = 40 };
            var result = Simulation.Run(NewSession(SessionMode.FullGuided, 900 + session, persona: persona, recall: recall, length: 6), SimulatedUser.Obedient, 900 + session);
            foreach (var module in result.State.Speech.Modules)
            {
                var m = LineLibrary.Default.Modules.First(x => x.Id == module);
                if (m.Cooldown == Speech.Cooldown.Session) continue;
                told.Add((session, module));
                usage[module] = new(1, start, session);
            }
        }
        foreach (var group in told.GroupBy(x => x.Module))
        {
            var cooldown = LineLibrary.Default.Modules.First(x => x.Id == group.Key).Cooldown;
            var sessions = group.Select(x => x.Session).OrderBy(x => x).ToList();
            int gap = cooldown switch { Speech.Cooldown.Once => int.MaxValue, Speech.Cooldown.Rare => 20, _ => 5 };
            for (int i = 1; i < sessions.Count; i++)
                Assert.True(sessions[i] - sessions[i - 1] >= gap, $"{group.Key} ({cooldown}) was told in sessions {string.Join(", ", sessions)}");
        }
        Assert.NotEmpty(told);
    }

    [Fact]
    public void TheCheckpoint_KeepsHerPlanAndWhatSheHasSaid()
    {
        var s = NewSession(SessionMode.FullGuided, 5);
        var result = Simulation.Run(s, SimulatedUser.Obedient, 5, limit: TimeSpan.FromMinutes(6));
        var json = JsonSerializer.Serialize(result.State, GuidedRecovery.Json);
        var back = JsonSerializer.Deserialize<SessionState>(json, GuidedRecovery.Json)!;
        Assert.Equal(result.State.Plan!.Segments.Select(x => (x.Type, x.Steps)), back.Plan!.Segments.Select(x => (x.Type, x.Steps)));
        Assert.Equal(result.State.Plan.Index, back.Plan.Index);
        Assert.Equal(result.State.Speech.Used, back.Speech.Used);
        Assert.Equal(result.State.Ending.Draw, back.Ending.Draw);
        Assert.Equal(JsonSerializer.Serialize(result.State.Persona), JsonSerializer.Serialize(back.Persona));
    }

    [Fact]
    public void Homework_IsHerPlanWrittenOut()
    {
        var s = NewSession(SessionMode.OwnContent, 11, length: 3);
        var result = Simulation.Run(s, SimulatedUser.Obedient with { Offers = "decline", FailsChallenges = 0 }, 11, chatter: false);
        var steps = result.Lines.Where(x => x.Directive is not null).ToList();
        Assert.True(steps.Count >= 4);
        Assert.DoesNotContain(result.Lines, x => x.Who == "Her (aside)");
        Assert.False(result.State.Active);
    }
}
