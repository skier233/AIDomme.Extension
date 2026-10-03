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
    /// <summary>His true body, which she has to find out: pace (a factor on every method's time to edge),
    /// recovery (seconds for closeness to halve at rest) and tolerance (seconds near the edge before he goes over).</summary>
    public double Pace { get; init; } = 1.15;
    public double Recovery { get; init; } = 30;
    public double Tolerance { get; init; } = 70;
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
    /// <summary>Edges per minute in free play.</summary>
    public double FreePlayEdgesPerMinute { get; init; } = 0.8;

    public static readonly SimulatedUser Obedient = new();
    public static readonly SimulatedUser Beggar = new() { Name = "begs to cum", BegEvery = 90, Pace = 0.8, Recovery = 35, Tolerance = 50 };
    public static readonly SimulatedUser Soft = new() { Name = "begs to stop", BegRestOnTorture = 0.8, Offers = "decline", Pace = 1.3, Recovery = 20, Tolerance = 60 };
    public static readonly SimulatedUser Silent = new() { Name = "only edges", Offers = "ignore", FailsChallenges = 0.1, Pace = 1.0, Recovery = 25, Tolerance = 90 };
    public static readonly SimulatedUser Weak = new() { Name = "fails and slips", Pace = 0.6, Recovery = 40, Tolerance = 18, FailsChallenges = 0.9, Luck = 0.2, BegEvery = 240, Offers = "decline" };
    public static readonly SimulatedUser[] All = [Obedient, Beggar, Soft, Silent, Weak];
}

/// <summary>One line of a simulated session. <see cref="Turn"/> groups the lines one event produced.</summary>
public sealed record TranscriptLine(TimeSpan At, string Who, string Text, SpeechAct? Act = null, string? Segment = null, string? Note = null,
    Directive? Directive = null, int Turn = 0, string? Module = null);

public sealed record SimulationResult(SessionState State, IReadOnlyList<TranscriptLine> Lines, string PlanAtStart, IReadOnlyList<string> Revisions)
{
    /// <summary>What he was actually told to do, step by step, with how long each took.</summary>
    public IReadOnlyList<StepRecord> Steps { get; init; } = [];
    /// <summary>The simulated user's true state at the end, and who he was: what her estimate is checked against.</summary>
    public BodyState? Truth { get; init; }
    public SimulatedUser? User { get; init; }
}

/// <summary>The four states at one moment, 0 to 1.</summary>
public readonly record struct BodySnapshot(double Closeness, double Need, double Control, double Overwhelm)
{
    public static BodySnapshot Of(BodyState b) => new(b.Closeness, b.Need, b.Control, b.Overwhelm);
}

/// <summary>One step of a simulated session as it was given and as it went.</summary>
public sealed record StepRecord(TimeSpan Start, TimeSpan End, string? Segment, Directive Directive, string? Game, bool LubeWait, int Edges)
{
    /// <summary>Her estimate of his state when the step began and ended, and (in simulation) the truth.</summary>
    public BodySnapshot EstimateStart { get; init; }
    public BodySnapshot EstimateEnd { get; init; }
    public BodySnapshot TruthStart { get; init; }
    public BodySnapshot TruthEnd { get; init; }
}

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

        bool slipped = false;
        // His true body. The same model runs her estimate; only the profile differs, and she has to learn it.
        var truth = new BodyState { AsOfUtc = start };
        var trueProfile = new ProfileState { Pace = user.Pace, Recovery = user.Recovery, Tolerance = user.Tolerance };
        var stepProfile = trueProfile.Copy();
        bool pushes = false;
        double TruthCap() => UserAction(s, user) switch
        {
            "edge" => 1,
            "maybe-edge" or "maybe-fail" => pushes ? 1 : Body.TimedCap,
            _ => s.Current?.Category == StepCategory.FreePlay ? Body.SelfPacedCap : Body.TimedCap,
        };
        // Asked for several edges in one go, he takes his own breather between them when he needs it.
        DateTime pausedUntil = start;
        void MoveTo(DateTime at)
        {
            double seconds = (at - clock.UtcNow).TotalSeconds;
            var mode = clock.UtcNow < pausedUntil ? BodyMode.Rest : SessionEngine.ModeOf(s);
            if (seconds > 0) Body.Advance(truth, stepProfile, mode, s.ActiveMethod, s.ActiveSpeed, seconds, TruthCap());
            if (at > clock.UtcNow) clock.UtcNow = at;
        }
        // When his body, left to what it is doing now, reaches the edge or gives out.
        (double? Edge, double? Slip) Ahead()
        {
            var mode = SessionEngine.ModeOf(s);
            if (mode == BodyMode.Rest || clock.UtcNow < pausedUntil) return (null, null);
            var b = truth.Copy();
            double cap = TruthCap();
            bool canSlip = !slipped && !s.PermissionGranted;
            for (int second = 1; second <= 1800; second++)
            {
                Body.Advance(b, stepProfile, mode, s.ActiveMethod, s.ActiveSpeed, 1, cap);
                if (canSlip && b.Control <= 0 && b.Closeness >= Body.NearEdge) return (null, second);
                if (mode == BodyMode.Stroke && b.Closeness >= 0.999) return (second, null);
            }
            return (null, null);
        }

        int turn = 0;
        // The states just before the last event, which is where the step that event ended stood.
        BodySnapshot estimateBefore = default, truthBefore = default;
        // The steps as they happen: a new record whenever what he is told to do changes.
        var steps = new List<StepRecord>();
        string? stepSignature = null;
        int edgesAtStep = 0;
        void TrackStep()
        {
            var at = clock.UtcNow - start;
            string? signature = s.Current is { } c && s.Active
                ? $"{s.StepNumber}|{c.Kind}|{c.Category}|{c.Method?.Id}|{c.Speed}|{c.DurationOrCount}|{c.HoldSeconds}|{s.Game?.Type}|{s.PendingLube is not null}"
                : null;
            // Free play's quiet check-ins are one long stretch of his own doing.
            if (signature is not null && s.Current!.Category == StepCategory.FreePlay && steps.Count > 0 && steps[^1].Directive.Category == StepCategory.FreePlay
                && stepSignature?.Contains("|FreePlay|") == true)
            {
                stepSignature = signature;
                return;
            }
            if (signature == stepSignature) return;
            if (steps.Count > 0 && steps[^1].End == TimeSpan.MinValue)
                steps[^1] = steps[^1] with { End = at, Edges = s.EdgesThisSession - edgesAtStep, EstimateEnd = estimateBefore, TruthEnd = truthBefore };
            stepSignature = signature;
            if (signature is null) return;
            edgesAtStep = s.EdgesThisSession;
            steps.Add(new(at, TimeSpan.MinValue, s.Current!.Segment, s.Current with { Text = "" }, s.Game?.Type.ToString(),
                s.PendingLube is not null && s.Current.Category == StepCategory.Rest && s.TimerDueUtc is null, 0)
                { EstimateStart = BodySnapshot.Of(s.Body), TruthStart = BodySnapshot.Of(truth) });
        }
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
            TrackStep();
        }
        void Before()
        {
            estimateBefore = BodySnapshot.Of(engine.Estimate(s));
            truthBefore = BodySnapshot.Of(truth);
        }
        void User(string what)
        {
            lines.Add(new(clock.UtcNow - start, "Him", what));
            Before();
        }
        void Edged() => Body.OnEdge(truth, trueProfile, learn: false, (clock.UtcNow - start).TotalSeconds);

        Say(engine.Begin(s));
        string planAtStart = PlanText(s);
        DateTime? nextBeg = user.BegEvery > 0 ? start.AddSeconds(user.BegEvery) : null;
        DateTime? nextFreeEdge = null;
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
                // No two runs are alike: his pace wanders a little from step to step.
                stepProfile = trueProfile.Copy();
                stepProfile.Pace *= 0.85 + userRandom.NextDouble() * 0.3;
                pushes = userRandom.NextDouble() < (d is { ForbidEdge: true } ? user.FailsChallenges : 0.4);
                if (d is { Category: StepCategory.Torment } && user.BegRestOnTorture > 0 && userRandom.NextDouble() < user.BegRestOnTorture)
                {
                    MoveTo(now.AddSeconds(6));
                    User("Please, can I rest?");
                    Say(engine.RequestRest(s));
                    continue;
                }
            }
            var candidates = new List<(DateTime At, string What)>();
            if (s.TimerDueUtc is DateTime due) candidates.Add((due, "timer"));
            if (chatter && voice.NextChatter(s, now) is DateTime talk) candidates.Add((talk, "chatter"));
            if (now < pausedUntil) candidates.Add((pausedUntil, "ready"));
            var (edgeIn, slipIn) = Ahead();
            if (UserAction(s, user) is { } action)
            {
                // An edge comes when his body gets there; everything else when he gets round to it.
                if (action is "edge" or "maybe-edge" or "maybe-fail")
                {
                    if (edgeIn is double seconds) candidates.Add((now.AddSeconds(seconds), "edge!"));
                    // Nothing to time it by, or a grip that will not quite take him there: he manages it in the end.
                    else if (action == "edge" && slipIn is null && now >= pausedUntil) candidates.Add((SessionEngine.ModeOf(s) != BodyMode.Stroke ? stepAction : stepAction.AddSeconds(200), "edge!"));
                }
                else candidates.Add((stepAction, action));
            }
            if (slipIn is double slip) candidates.Add((now.AddSeconds(slip), "slip"));
            if (nextBeg is DateTime beg && s.Progress > 0.33 && s.Offer is null && s.Game is null && !s.FinishDecided) candidates.Add((beg, "beg"));
            if (!s.Contract.GamesOnly && s.Plan is null && s.Current?.Category == StepCategory.FreePlay)
            {
                nextFreeEdge ??= now.AddSeconds(60 / user.FreePlayEdgesPerMinute * (0.6 + userRandom.NextDouble() * 0.8));
                candidates.Add((nextFreeEdge.Value, "edge"));
            }
            if (candidates.Count == 0) break;
            var (at, what) = candidates.MinBy(x => x.At);
            MoveTo(at < now ? now : at);
            switch (what)
            {
                case "timer":
                    Before();
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
                    Body.OnOrgasm(truth);
                    Say(engine.Orgasm(s, allowed: false));
                    break;
                case "edge":
                case "edge!":
                    nextFreeEdge = null;
                    User("(edge)");
                    Edged();
                    int stepBefore = s.StepNumber;
                    Say(engine.Edge(s));
                    // More edges wanted in the same step, and he is close to going over: he waits until he can.
                    if (s.StepNumber == stepBefore && s.Active && UserAction(s, user) == "edge" && truth.Control < 0.6)
                        pausedUntil = clock.UtcNow.AddSeconds(Math.Max(5, Body.RestToRegain(trueProfile, truth.Need, truth.Closeness, truth.Control, 0.7)));
                    lastStep = -1;
                    break;
                case "ready":
                    break;
                case "cum":
                    User("(cums, with permission)");
                    Body.OnOrgasm(truth);
                    Say(engine.Orgasm(s, allowed: true));
                    lastStep = -1;
                    break;
                default:
                    Act(engine, s, what, user, userRandom, User, Say);
                    lastStep = -1;
                    break;
            }
        }
        if (s.Active) lines.Add(new(clock.UtcNow - start, "Note", "the simulation stopped here"));
        if (steps.Count > 0 && steps[^1].End == TimeSpan.MinValue)
            steps[^1] = steps[^1] with { End = clock.UtcNow - start, Edges = s.EdgesThisSession - edgesAtStep, EstimateEnd = BodySnapshot.Of(s.Body), TruthEnd = BodySnapshot.Of(truth) };
        return new(s, lines, planAtStart, s.Plan?.Revisions.ToList() ?? []) { Steps = steps, Truth = truth, User = user };
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
        // Fetching the lube, or an edge with nothing to time it by.
        return 15 + r.NextDouble() * 25;
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
        sb.AppendLine($"His profile as she starts: pace {s.Profile.Pace:0.00} ({s.Profile.PaceSource}), recovery {s.Profile.Recovery:0}s ({s.Profile.RecoverySource}), tolerance {s.Profile.Tolerance:0}s ({s.Profile.ToleranceSource})");
        if (view.Aims.Count == 0) sb.AppendLine("No aims: he sets the pace, she decides the ending.");
        else sb.AppendLine("Her aims (segments are chosen as she goes, from his state):");
        int n = 0;
        foreach (var aim in view.Aims)
            sb.AppendLine($"  {++n}. {aim.Name}{(aim.Written.Length > 0 ? $", as \"{aim.Written}\"" : "")}, {aim.Share:P0} of the session{(aim.Origin == "plan" ? "" : $" [{aim.Origin}]")}. She wants: {aim.Goal}. Why: {aim.Why}.");
        return sb.ToString();
    }

    /// <summary>Every aim and the segments inside it: what each was for, the parameters she chose, why she chose it and why it ended.</summary>
    public static string AimsAndSegments(SimulationResult result)
    {
        var s = result.State;
        var sb = new StringBuilder();
        if (s.Plan is not { } plan || plan.Aims.Count == 0) return "";
        var view = SessionHost.PlanOf(s);
        string At(DateTime? t) => t is DateTime at ? Clock(at - s.StartedAtUtc) : "";
        sb.AppendLine("## Aims and segments");
        for (int i = 0; i < plan.Aims.Count; i++)
        {
            var aim = plan.Aims[i];
            sb.AppendLine();
            sb.AppendLine($"**Aim {i + 1}: {view.Aims[i].Name}**{(view.Aims[i].Written.Length > 0 ? $" (written: {view.Aims[i].Written})" : "")}, {aim.Share:P0} of the session{(aim.Origin == "plan" ? "" : $" [{aim.Origin}]")}, {(aim.Started ? $"{At(aim.StartedUtc)} to {At(aim.EndedUtc ?? s.LastActivityUtc)}" : "never begun")}");
            sb.AppendLine($"- She wanted: {aim.Goal}{(aim.Achieved ? " (reached)" : "")}");
            sb.AppendLine($"- Why she chose it: {aim.Why}");
            if (aim.Ended.Length > 0) sb.AppendLine($"- Why it ended: {aim.Ended}");
            var segments = plan.Segments.Where(x => x.Aim == i && x.Started && x.Type != SegmentCatalogue.Ending).ToList();
            if (segments.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine("| At | Took | Segment | Purpose | Role | Kind | Parameters | Why she chose it | Why it ended |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var seg in segments)
            {
                var took = (seg.EndedUtc ?? s.LastActivityUtc) - (seg.StartedUtc ?? s.StartedAtUtc);
                sb.AppendLine($"| {At(seg.StartedUtc)} | {Span(took)} | {SegmentCatalogue.Default.Find(seg.Type)?.Name ?? seg.Type} | {Purposes.Name(seg.Purpose)} | {seg.Role} | {(seg.Written ? "written" : "improvised")} " +
                    $"| {seg.Parameters()} | {seg.Reason}{(seg.Origin == "plan" ? "" : $" [{seg.Origin}]")} | {seg.Ended} |");
            }
        }
        sb.AppendLine();
        var byPurpose = plan.Segments.Where(x => x.Started && x.Type != SegmentCatalogue.Ending)
            .GroupBy(x => x.Purpose)
            .Select(g => (g.Key, Time: g.Aggregate(TimeSpan.Zero, (sum, x) => sum + ((x.EndedUtc ?? s.LastActivityUtc) - (x.StartedUtc ?? s.StartedAtUtc)))))
            .OrderByDescending(x => x.Time).ToList();
        if (byPurpose.Count > 0) sb.AppendLine($"**Time by purpose:** {string.Join(", ", byPurpose.Select(x => $"{Purposes.Name(x.Key)} {Span(x.Time)}"))}.");
        int written = plan.Segments.Count(x => x.Started && x.Written && x.Type != SegmentCatalogue.Ending), all = plan.Segments.Count(x => x.Started && x.Type != SegmentCatalogue.Ending);
        sb.AppendLine($"**Segments:** {all}, of which {written} written.");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>A readable transcript: the plan, the session as chat with his actions, and how the plan changed.</summary>
    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    private static string Span(TimeSpan t) => t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    private static string Seconds(int s) => s < 60 ? $"{s}s" : $"{s / 60}:{s % 60:00}";

    /// <summary>What a step asked of him, in a few words.</summary>
    public static string Describe(StepRecord step)
    {
        var d = step.Directive;
        string method = d.Method is { IsUserChoice: false } m ? m.Name : d.Method is not null ? "his choice of grip" : "";
        string pace = d.Speed > 0 ? $"speed {d.Speed}" : d.Method is not null ? "his pace" : "";
        string how = string.Join(", ", new[] { method, pace }.Where(x => x.Length > 0));
        if (step.Game is { } game) return $"Game: {game}" + (how.Length > 0 ? $" ({how})" : "");
        if (step.LubeWait) return "Get the lube";
        return d.Category switch
        {
            StepCategory.FreePlay => "Free play: his grip, his pace",
            StepCategory.Offer => "A deal on the table",
            StepCategory.Hold => $"Hold at the edge {Seconds(d.DurationOrCount)}",
            StepCategory.Rest or StepCategory.Psych when d.Method is null => $"Hands off {Seconds(d.DurationOrCount)}",
            StepCategory.Finish when d.Kind == InstructionKind.TimedMethod && d.Method is not null => $"The ending: {how}, {Seconds(d.DurationOrCount)} to cum",
            StepCategory.Finish when d.Kind == InstructionKind.TimedMethod => "The ending: a punishment ending",
            StepCategory.Finish => "The ending: no orgasm",
            _ => d.Kind switch
            {
                InstructionKind.EdgeCount => $"{how}: {(d.DurationOrCount == 1 ? "1 edge" : $"{d.DurationOrCount} edges")}"
                    + (d.HoldSeconds > 0 ? $", hold the last {Seconds(d.HoldSeconds)}" : ""),
                InstructionKind.TimedMethod => $"{how} for {Seconds(d.DurationOrCount)}"
                    + (d.ForbidEdge ? ", no edging" : d.CountEdges ? ", as many edges as he can" : ""),
                InstructionKind.Rest => $"Hands off {Seconds(d.DurationOrCount)}",
                _ => how.Length > 0 ? how : d.Category.ToString(),
            },
        };
    }

    /// <summary>The session's content at a glance: every step, how long it took, and the totals.</summary>
    private static string WhatHappened(SimulationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## What happened");
        sb.AppendLine("Closeness is her estimate at the start and end of the step, then his true closeness; need, control and overwhelm are her estimates. Gap is the hands-off time just before.");
        sb.AppendLine();
        sb.AppendLine("| At | Took | Segment | What she had him do | Edges | Gap | Close (est) | Close (true) | Need | Control | Overwhelm |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        static string Pair(double a, double b) => $"{a * 100:0}→{b * 100:0}";
        TimeSpan gap = TimeSpan.Zero;
        string? segment = null;
        var stimulation = new Dictionary<string, TimeSpan>();
        TimeSpan handsOff = TimeSpan.Zero, holds = TimeSpan.Zero, games = TimeSpan.Zero;
        foreach (var step in result.Steps)
        {
            var took = step.End - step.Start;
            string seg = step.Segment is { } id && id != segment ? SegmentCatalogue.Default.Find(id)?.Name ?? id : "";
            if (step.Segment is not null) segment = step.Segment;
            var d = step.Directive;
            bool handsOffStep = step.Game is null && d.Category != StepCategory.Hold && (d.Method is null || d.Category is StepCategory.Rest or StepCategory.Offer);
            sb.AppendLine($"| {Clock(step.Start)} | {Span(took)} | {seg} | {Describe(step)} | {(step.Edges > 0 ? step.Edges : "")} | {(!handsOffStep && gap > TimeSpan.Zero ? Span(gap) : "")} " +
                $"| {Pair(step.EstimateStart.Closeness, step.EstimateEnd.Closeness)} | {Pair(step.TruthStart.Closeness, step.TruthEnd.Closeness)} " +
                $"| {Pair(step.EstimateStart.Need, step.EstimateEnd.Need)} | {Pair(step.EstimateStart.Control, step.EstimateEnd.Control)} | {Pair(step.EstimateStart.Overwhelm, step.EstimateEnd.Overwhelm)} |");
            gap = handsOffStep ? gap + took : TimeSpan.Zero;
            if (step.Game is not null) games += took;
            else if (d.Category == StepCategory.Hold) holds += took;
            else if (d.Method is not null && d.Category is not (StepCategory.Rest or StepCategory.Offer))
            {
                string name = d.Method.IsUserChoice ? "his choice" : d.Method.Name;
                stimulation[name] = stimulation.GetValueOrDefault(name) + took;
            }
            else if (d.Category is StepCategory.Rest or StepCategory.Psych) handsOff += took;
        }
        var total = result.Steps[^1].End - result.Steps[0].Start;
        var stim = stimulation.Values.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        sb.AppendLine();
        sb.AppendLine($"**Totals:** {Span(total)} in all; stroking {Span(stim)}, hands off {Span(handsOff)}, holding at the edge {Span(holds)}, games {Span(games)}; " +
            $"{result.State.EdgesThisSession} edges; {result.State.Outcome}.");
        if (stimulation.Count > 0)
            sb.AppendLine($"**By method:** {string.Join(", ", stimulation.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {Span(x.Value)}"))}.");
        sb.AppendLine();
        sb.Append(BodyReport(result));
        return sb.ToString();
    }

    /// <summary>His state over the session, what she believed about his body, and how good her estimate was.</summary>
    public static string BodyReport(SimulationResult result)
    {
        var sb = new StringBuilder();
        var b = result.State.Body;
        var p = result.State.Profile;
        static string T(double seconds) => Span(TimeSpan.FromSeconds(seconds));
        sb.AppendLine("## His state");
        sb.AppendLine($"**Time by closeness (her estimate):** under 50% {T(b.SecondsBelow50)}, 50 to 75% {T(b.Seconds50To75)}, 75 to 90% {T(b.Seconds75To90)}, over 90% {T(b.SecondsAbove90)}; longest stretch over 80% {T(b.LongestAbove80)}.");
        if (result.Truth is { } truth)
            sb.AppendLine($"**Time by closeness (true):** under 50% {T(truth.SecondsBelow50)}, 50 to 75% {T(truth.Seconds50To75)}, 75 to 90% {T(truth.Seconds75To90)}, over 90% {T(truth.SecondsAbove90)}; longest stretch over 80% {T(truth.LongestAbove80)}.");
        sb.AppendLine($"**Peaks (her estimate):** need {b.PeakNeed:P0}, overwhelm {b.PeakOverwhelm:P0}, lowest control {b.LowestControl:P0}, time with control under 35% {T(b.SecondsNearLimit)}." +
            (result.Truth is { } t ? $" **True:** need {t.PeakNeed:P0}, overwhelm {t.PeakOverwhelm:P0}, lowest control {t.LowestControl:P0}, under 35% for {T(t.SecondsNearLimit)}." : ""));
        sb.AppendLine();
        sb.AppendLine("**His profile as she has it at the end**" + (result.User is null ? "" : " (and the simulated truth)"));
        sb.AppendLine();
        sb.AppendLine("| | She believes | Source |" + (result.User is null ? "" : " True |"));
        sb.AppendLine("|---|---|---|" + (result.User is null ? "" : "---|"));
        sb.AppendLine($"| Pace (factor on time to edge) | {p.Pace:0.00} | {p.PaceSource} |" + (result.User is { } u1 ? $" {u1.Pace:0.00} |" : ""));
        sb.AppendLine($"| Recovery (seconds to halve) | {p.Recovery:0} | {p.RecoverySource} |" + (result.User is { } u2 ? $" {u2.Recovery:0} |" : ""));
        sb.AppendLine($"| Tolerance (seconds near the edge) | {p.Tolerance:0} | {p.ToleranceSource} |" + (result.User is { } u3 ? $" {u3.Tolerance:0} |" : ""));
        sb.AppendLine();
        if (b.Checks.Count > 0)
        {
            sb.AppendLine("**Estimator check:** each edge where she set the method and pace, as she expected it and as it came.");
            sb.AppendLine();
            sb.AppendLine("| At | Method | From (est) | Rest before | Expected | Actual | Off by |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var c in b.Checks)
                sb.AppendLine($"| {Clock(TimeSpan.FromSeconds(c.AtSeconds))} | {c.Method} | {c.ClosenessBefore:P0} | {(c.RestBefore >= 0 ? T(c.RestBefore) : "")} | {c.ExpectedSeconds:0}s | {c.ActualSeconds:0}s | {(c.ActualSeconds - c.ExpectedSeconds) / Math.Max(1, c.ExpectedSeconds):+0%;-0%} |");
            double error = b.Checks.Average(c => Math.Abs(c.ActualSeconds - c.ExpectedSeconds) / Math.Max(1, c.ExpectedSeconds));
            var late = b.Checks.Skip(b.Checks.Count / 2).ToList();
            double lateError = late.Average(c => Math.Abs(c.ActualSeconds - c.ExpectedSeconds) / Math.Max(1, c.ExpectedSeconds));
            sb.AppendLine();
            sb.AppendLine($"Average miss {error:P0} over {b.Checks.Count} edges; {lateError:P0} over the later half.");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string Transcript(SimulationResult result, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        if (result.Steps.Count > 0) sb.Append(WhatHappened(result));
        sb.AppendLine("## Her plan at the start");
        sb.AppendLine(result.PlanAtStart);
        sb.Append(AimsAndSegments(result));
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
