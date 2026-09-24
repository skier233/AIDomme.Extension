using System.Text;
using AIDome.Data;
using AIDome.Sessions.Plan;
using AIDome.Speech;

namespace AIDome.Sessions;

/// <summary>Randomness from a seed, so a simulated session can be replayed exactly.</summary>
public sealed class SeededRandom(int seed) : IRandom
{
    private readonly Random random = new(seed);
    public int Next(int min, int max) => random.Next(min, max);
    public double NextDouble() => random.NextDouble();
}

public sealed class SimulatedClock(DateTime start) : IClock
{
    public DateTime UtcNow { get; set; } = start;
}

/// <summary>How a simulated user behaves. Times are seconds; chances are 0–1.</summary>
public sealed record SimulatedUser
{
    public string Name { get; init; } = "obedient";
    /// <summary>Seconds to reach an edge at a middling pace.</summary>
    public double EdgeSeconds { get; init; } = 60;
    /// <summary>Ask to cum every this many seconds once past the first third (0: never).</summary>
    public double BegEvery { get; init; }
    /// <summary>Chance of begging for a rest when a torturous step starts.</summary>
    public double BegRestOnTorture { get; init; }
    /// <summary>What he does with her offers: accept, decline or ignore.</summary>
    public string Offers { get; init; } = "accept";
    /// <summary>Chance of edging during a "don't you dare" challenge.</summary>
    public double FailsChallenges { get; init; } = 0.3;
    /// <summary>Chance of winning a guessing game (answers the secret).</summary>
    public double Luck { get; init; } = 0.5;
    /// <summary>Chance of coming without permission once, late in the session.</summary>
    public double Slips { get; init; }
    /// <summary>Edges per minute in free play.</summary>
    public double FreePlayEdgesPerMinute { get; init; } = 0.8;

    public static readonly SimulatedUser Obedient = new();
    public static readonly SimulatedUser Beggar = new() { Name = "begs to cum", BegEvery = 90 };
    public static readonly SimulatedUser Soft = new() { Name = "begs to stop", BegRestOnTorture = 0.8, Offers = "decline" };
    public static readonly SimulatedUser Silent = new() { Name = "only edges", Offers = "ignore", FailsChallenges = 0.1 };
    public static readonly SimulatedUser Weak = new() { Name = "fails and slips", EdgeSeconds = 35, FailsChallenges = 0.9, Luck = 0.2, Slips = 1, BegEvery = 240, Offers = "decline" };
    public static readonly SimulatedUser[] All = [Obedient, Beggar, Soft, Silent, Weak];
}

/// <summary>One line of a simulated session. <see cref="Turn"/> groups the lines one event produced.</summary>
public sealed record TranscriptLine(TimeSpan At, string Who, string Text, SpeechAct? Act = null, string? Segment = null, string? Note = null,
    Directive? Directive = null, int Turn = 0, string? Module = null);

public sealed record SimulationResult(SessionState State, IReadOnlyList<TranscriptLine> Lines, string PlanAtStart, IReadOnlyList<string> Revisions);

/// <summary>
/// Runs a whole session with the real engine, planner and voice against a simulated user and a
/// fake clock: the transcripts used to judge her sessions and speech, and the way homework is made.
/// </summary>
public static class Simulation
{
    public static SimulationResult Run(SessionState s, SimulatedUser user, int seed, Voice? voice = null, Screen? screen = null,
        TimeSpan? limit = null, bool chatter = true)
    {
        voice ??= Voice.Default;
        var clock = new SimulatedClock(new DateTime(2026, 9, 23, 21, 0, 0, DateTimeKind.Utc));
        var random = new SeededRandom(seed);
        var userRandom = new SeededRandom(seed * 7919 + 13);
        var engine = new SessionEngine(random, clock);
        var lines = new List<TranscriptLine>();
        var start = clock.UtcNow;
        var end = start + (limit ?? TimeSpan.FromHours(2));
        screen = screen is null ? null : screen with { SeenAtUtc = start };

        int turn = 0;
        void Say(Transition t)
        {
            turn++;
            var c = Context(s, screen, random, clock.UtcNow, chatter);
            var spoken = voice.Speak(t.Say, c);
            if (s.Current is { } current && spoken.LastOrDefault(x => x.Instruction) is { } instruction) s.Current = current with { Text = instruction.Text };
            foreach (var line in spoken)
                lines.Add(new(clock.UtcNow - start, line.Stream == SpeechStream.Chatter ? "Her (aside)" : "Her", line.Text, line.Act, s.Current?.Segment,
                    Directive: line.Instruction ? s.Current : null, Turn: turn, Module: line.Module));
            foreach (var notice in t.Notices) lines.Add(new(clock.UtcNow - start, "Note", notice));
            // What she said about the stakes is her words; the transcript also shows the real numbers.
            foreach (var effect in t.Effects)
                switch (effect)
                {
                    case GameStarted { Game: var g } when g.Win.Count + g.Loss.Count > 0:
                        lines.Add(new(clock.UtcNow - start, "Note", $"{g.Type} stakes: win {Games.Mechanics(g.Win)}; lose {Games.Mechanics(g.Loss)}"));
                        break;
                    case GameFinished { Result: { } result }:
                        lines.Add(new(clock.UtcNow - start, "Note", $"game {(result.Neutral ? "over" : result.Won ? "won" : "lost")}: {Games.Mechanics(result.Consequences)}"));
                        break;
                    case OfferMade { Offer: var o }:
                        lines.Add(new(clock.UtcNow - start, "Note", $"offer: {o.Name}, {o.Kind} x{o.Amount}, worth {o.Points:0} points" + (o.AfterRefusals > 0 ? $", after {o.AfterRefusals} refusal(s)" : "")));
                        break;
                }
            if (t.Rejected is not null) lines.Add(new(clock.UtcNow - start, "Note", $"refused: {t.Rejected}"));
        }
        void User(string what) => lines.Add(new(clock.UtcNow - start, "Him", what));

        Say(engine.Begin(s));
        string planAtStart = PlanText(s);
        DateTime? nextBeg = user.BegEvery > 0 ? start.AddSeconds(user.BegEvery) : null;
        DateTime? nextFreeEdge = null;
        bool slipped = false;
        int lastStep = -1;
        DateTime stepAction = start;

        for (int guard = 0; guard < 5000 && s.Active && clock.UtcNow < end; guard++)
        {
            var now = clock.UtcNow;
            var d = s.Current;
            // Plan his next action for this step once, when the step changes.
            if (s.StepNumber != lastStep)
            {
                lastStep = s.StepNumber;
                stepAction = now.AddSeconds(ActionDelay(s, user, userRandom));
                if (d is { Category: StepCategory.Torment } && user.BegRestOnTorture > 0 && userRandom.NextDouble() < user.BegRestOnTorture)
                {
                    clock.UtcNow = now.AddSeconds(6);
                    User("Please, can I rest?");
                    Say(engine.RequestRest(s));
                    continue;
                }
            }
            var candidates = new List<(DateTime At, string What)>();
            if (s.TimerDueUtc is DateTime due) candidates.Add((due, "timer"));
            if (chatter && voice.NextChatter(s, now) is DateTime talk) candidates.Add((talk, "chatter"));
            if (UserAction(s, user) is { } action) candidates.Add((stepAction, action));
            if (nextBeg is DateTime beg && s.Progress > 0.33 && s.Offer is null && s.Game is null && !s.FinishDecided) candidates.Add((beg, "beg"));
            if (!s.Contract.GamesOnly && s.Plan is null && s.Current?.Category == StepCategory.FreePlay)
            {
                nextFreeEdge ??= now.AddSeconds(60 / user.FreePlayEdgesPerMinute * (0.6 + userRandom.NextDouble() * 0.8));
                candidates.Add((nextFreeEdge.Value, "edge"));
            }
            if (!slipped && user.Slips > 0 && s.Progress > 0.7 && !s.PermissionGranted && !s.FinishDecided) candidates.Add((now.AddSeconds(20), "slip"));
            if (candidates.Count == 0) break;
            var (at, what) = candidates.MinBy(x => x.At);
            clock.UtcNow = at < now ? now : at;
            switch (what)
            {
                case "timer":
                    Say(engine.Timer(s, s.TimerGeneration));
                    break;
                case "chatter":
                {
                    var c = Context(s, screen, random, clock.UtcNow);
                    turn++;
                    foreach (var line in voice.Tick(s, c))
                        lines.Add(new(clock.UtcNow - start, "Her (aside)", line.Text, line.Act, s.Current?.Segment, Turn: turn, Module: line.Module));
                    break;
                }
                case "beg":
                    User("May I cum?");
                    Say(engine.RequestPermission(s));
                    nextBeg = clock.UtcNow.AddSeconds(user.BegEvery * (0.7 + userRandom.NextDouble() * 0.6));
                    break;
                case "slip":
                    slipped = true;
                    User("(came without permission)");
                    Say(engine.Orgasm(s, allowed: false));
                    break;
                case "edge":
                    nextFreeEdge = null;
                    User("(edge)");
                    Say(engine.Edge(s));
                    lastStep = -1;
                    break;
                default:
                    Act(engine, s, what, user, userRandom, User, Say);
                    lastStep = -1;
                    break;
            }
        }
        if (s.Active) lines.Add(new(clock.UtcNow - start, "Note", "the simulation stopped here"));
        return new(s, lines, planAtStart, s.Plan?.Revisions.ToList() ?? []);
    }

    /// <summary>What he does next in this step, if anything (timers take care of the rest).</summary>
    private static string? UserAction(SessionState s, SimulatedUser user)
    {
        var d = s.Current;
        if (d is null) return null;
        if (s.Offer is not null) return user.Offers == "ignore" ? null : "offer";
        if (s.Game is { } game)
            return game.Input != GameInput.None && game.Choices.Count > 0 || game.Input == GameInput.Number ? "answer"
                : game.Type is GameType.RaceToEdge or GameType.EdgeRush or GameType.EdgeEndurance or GameType.EdgeWager && (game.Type != GameType.EdgeEndurance || game.Phase == 0) ? "edge" : null;
        if (d.Kind == InstructionKind.Finish) return "done";
        if (d.Category == StepCategory.Finish && s.PermissionGranted) return "cum";
        if (s.PendingLube is not null && d.Category == StepCategory.Rest && s.TimerDueUtc is null) return "done";
        if (s.Price is not null && !s.Holding) return "edge";
        if (d.Kind == InstructionKind.EdgeCount && !s.Holding) return "edge";
        if (d.CountEdges) return "edge";
        if (d.ForbidEdge && s.EdgesThisStep == 0) return "maybe-fail";
        if (d.Kind == InstructionKind.TimedMethod && d.Category is StepCategory.Edge or StepCategory.Surprise && d.Method is not null) return "maybe-edge";
        return null;
    }

    private static double ActionDelay(SessionState s, SimulatedUser user, IRandom r)
    {
        var d = s.Current;
        if (s.Offer is not null || s.Game is { Input: not GameInput.None } || d?.Kind == InstructionKind.Finish) return 3 + r.NextDouble() * 5;
        if (d?.Category == StepCategory.Finish) return 10 + r.NextDouble() * 20;
        double pace = d?.Speed is > 0 and var speed ? 1.4 - speed * 0.08 : 1;
        double closeness = d?.Method is { IsUserChoice: false } m ? 1.3 - m.Closeness * 0.06 : 1;
        return user.EdgeSeconds * pace * closeness * (0.6 + r.NextDouble() * 0.8);
    }

    private static void Act(SessionEngine engine, SessionState s, string what, SimulatedUser user, IRandom r, Action<string> said, Action<Transition> say)
    {
        switch (what)
        {
            case "offer":
                if (user.Offers == "accept") { said("I accept."); say(engine.AcceptOffer(s)); }
                else { said("No."); say(engine.DeclineOffer(s)); }
                break;
            case "answer":
            {
                var game = s.Game!;
                int answer = game.Input == GameInput.Number
                    ? (r.NextDouble() < user.Luck ? game.Secret : game.Secret + 1)
                    : r.NextDouble() < user.Luck && game.Choices.Any(x => x.Value == game.Secret) ? game.Secret : game.Choices[r.Next(0, game.Choices.Count)].Value;
                said($"(answers: {game.Choices.FirstOrDefault(x => x.Value == answer)?.Label ?? answer.ToString()})");
                say(engine.Answer(s, answer));
                break;
            }
            case "done":
                said("(done)");
                say(engine.Complete(s));
                break;
            case "cum":
                said("(cums, with permission)");
                say(engine.Orgasm(s, allowed: true));
                break;
            case "maybe-fail":
                if (r.NextDouble() < user.FailsChallenges) { said("(edge)"); say(engine.Edge(s)); }
                break;
            case "maybe-edge":
                if (r.NextDouble() < 0.4) { said("(edge)"); say(engine.Edge(s)); }
                break;
            default:
                said("(edge)");
                say(engine.Edge(s));
                break;
        }
    }

    public static NarrationContext Context(SessionState s, Screen? screen, IRandom random, DateTime now, bool chatter = true) => new()
    {
        Facts = Knowledge.Facts(s, screen is null ? null : screen with { SeenAtUtc = now }, now),
        Memory = s.Speech, Recall = s.Recall, Persona = s.Persona, Random = random, Now = now, Chatter = chatter,
    };

    public static string PlanText(SessionState s)
    {
        var sb = new StringBuilder();
        var view = SessionHost.PlanOf(s);
        sb.AppendLine($"Mode {s.Contract.Mode}; intensity {s.Intensity}, length {s.Length}, denial {s.Denial}, meanness {s.Mean}{(s.Contract.Punishment ? "; punishment session" : "")}");
        sb.AppendLine($"Persona tonight: harshness {s.Persona.Harshness}, humiliation {s.Persona.Humiliation}, manipulation {s.Persona.Manipulation}, talkativeness {s.Persona.Talkativeness}");
        sb.AppendLine($"Mood: {view.Mood} ({view.MoodCause}), annoyance {view.Annoyance}");
        sb.AppendLine($"Ending she leans toward: {view.Ending} ({view.EndingReason}); {view.Presentation switch { "Honest" => "she will be honest about it", "Lie" => "she may lie about it", _ => "she will keep it to herself" }}");
        if (view.Segments.Count == 0) sb.AppendLine("No plan of segments: he sets the pace, she decides the ending.");
        foreach (var seg in view.Segments)
            sb.AppendLine($"  - {seg.Name} ({seg.Type}), {seg.Share:P0} of the budget{(seg.Origin == "plan" ? "" : $" [{seg.Origin}]")}: {seg.Goal}");
        return sb.ToString();
    }

    /// <summary>A readable transcript: the plan, the session as chat with his actions, and how the plan changed.</summary>
    public static string Transcript(SimulationResult result, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine("## Her plan at the start");
        sb.AppendLine(result.PlanAtStart);
        sb.AppendLine("## Session");
        string? segment = null;
        foreach (var line in result.Lines)
        {
            if (line.Segment is { } seg && seg != segment && line.Who.StartsWith("Her"))
            {
                segment = seg;
                sb.AppendLine($"  -- segment: {SegmentCatalogue.Default.Find(seg)?.Name ?? seg} --");
            }
            sb.AppendLine($"[{(int)line.At.TotalMinutes:00}:{line.At.Seconds:00}] {line.Who}: {line.Text}");
        }
        sb.AppendLine();
        sb.AppendLine($"Outcome: {result.State.Outcome}; {result.State.EdgesThisSession} edges; {result.State.Points:0}/{result.State.Budget:0} points; " +
            $"asked to cum {result.State.RequestsThisSession} times, to rest {result.State.RestRequests} times.");
        if (result.Revisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## How the plan changed");
            foreach (var revision in result.Revisions) sb.AppendLine($"  - {revision}");
        }
        return sb.ToString();
    }
}
