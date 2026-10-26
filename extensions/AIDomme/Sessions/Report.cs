using System.Text;
using AIDomme.Sessions.Plan;

namespace AIDomme.Sessions;

/// <summary>What one simulated session came to, in numbers, with anything that looks wrong flagged.</summary>
public sealed record SessionMetrics
{
    public string Name { get; init; } = "";
    public string Mode { get; init; } = "";
    public string User { get; init; } = "";
    public string Outcome { get; init; } = "";
    /// <summary>The session's sliders: intensity/length/denial/meanness.</summary>
    public string Sliders { get; init; } = "";
    public double Minutes { get; init; }
    public double TargetMinutes { get; init; }
    /// <summary>Why it ended well short of its length, when it did (a deal, permission, a slip).</summary>
    public string EndedEarly { get; init; } = "";
    public int Edges { get; init; }
    public int Slips { get; init; }
    public int AskedToCum { get; init; }
    public int AskedToRest { get; init; }
    public ProfileState Believed { get; init; } = new();
    public SimulatedUser? Truth { get; init; }
    public int EdgeChecks { get; init; }
    /// <summary>How far off her expected time to each edge was, on average, over all edges and over the later half.</summary>
    public double Miss { get; init; }
    public double LateMiss { get; init; }
    public double LowestControl { get; init; }
    public double? LowestControlTrue { get; init; }
    /// <summary>Her estimate of his control minus the truth, on average, in points of 100. Below zero she is being careful; above it she thinks he has more left than he does.</summary>
    public double ControlGap { get; init; }
    public double PeakNeed { get; init; }
    public double PeakOverwhelm { get; init; }
    public IReadOnlyList<(Purpose Purpose, double Seconds)> Purposes { get; init; } = [];
    public int Aims { get; init; }
    public int Segments { get; init; }
    public int Written { get; init; }
    public IReadOnlyList<string> Flags { get; init; } = [];
}

/// <summary>
/// Transcripts of simulated sessions, written to be read quickly: the settings a session started
/// from, what it came to, the methods used, its flow (aims, segments, steps) and the chat. Because
/// the user is simulated, how her estimates did against him is left out unless detail is asked for.
/// All tables are fixed-width text so they read the same in a plain editor and a markdown viewer.
/// <see cref="Summary"/> puts a whole run of sessions on one page.
/// </summary>
public static class SessionReport
{
    private static string Clock(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    private static string Span(TimeSpan t) => t.TotalSeconds < 60 ? $"{(int)t.TotalSeconds}s" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    private static string Span(double seconds) => Span(TimeSpan.FromSeconds(seconds));
    private static string Pct(double v) => $"{v * 100:0}%";
    private static string Fit(string text, int width) => text.Length > width ? text[..(width - 1)] + "…" : text.PadRight(width);
    private static string Pair(double a, double b) => $"{a * 100:0}>{b * 100:0}";
    private static string Short(Purpose p) => p switch { Purpose.Ache => "ache", Purpose.Control => "control", Purpose.Overwhelm => "overwhelm", Purpose.Starve => "starve", _ => "cool" };

    // ── The numbers ──

    public static SessionMetrics Measure(SimulationResult result, string name)
    {
        var s = result.State;
        var b = s.Body;
        var plan = s.Plan;
        var steps = result.Steps;
        double minutes = steps.Count > 0 ? (steps[^1].End - steps[0].Start).TotalMinutes : 0;
        var checks = b.Checks.Where(c => c.ExpectedSeconds < Body.Unreachable).ToList();
        static double Off(EdgeCheck c) => Math.Abs(c.ActualSeconds - c.ExpectedSeconds) / Math.Max(1, c.ExpectedSeconds);
        var started = plan?.Segments.Where(x => x.Started && x.Type != SegmentCatalogue.Ending).ToList() ?? [];
        var purposes = started.GroupBy(x => x.Purpose)
            .Select(g => (g.Key, g.Sum(x => ((x.EndedUtc ?? s.LastActivityUtc) - (x.StartedUtc ?? s.StartedAtUtc)).TotalSeconds)))
            .OrderByDescending(x => x.Item2).ToList();
        double gap = result.Truth is null || steps.Count == 0 ? 0 : steps.Average(x => x.EstimateEnd.Control - x.TruthEnd.Control) * 100;
        bool finished = s.Budget > 0 && s.Points >= s.Budget * 0.8;
        string early = s.Active ? "the simulation stopped"
            : finished || plan is null ? ""
            : s.UnauthorizedOrgasms > 0 && s.Outcome == Data.SessionOutcome.UnauthorizedOrgasm ? "a slip"
            : s.Outcome == Data.SessionOutcome.AllowedOrgasm ? (s.RequestsThisSession > 0 ? "he asked to cum and she agreed, or sold it to him" : "she let him cum early")
            : "";

        var m = new SessionMetrics
        {
            Name = name, Sliders = $"{s.Intensity}/{s.Length}/{s.Denial}/{s.Mean}", Mode = s.Contract.Mode.ToString(), User = result.User?.Name ?? "", Outcome = s.Outcome.ToString(),
            Minutes = minutes, TargetMinutes = s.BudgetMinutes, EndedEarly = early,
            Edges = s.EdgesThisSession, Slips = s.UnauthorizedOrgasms, AskedToCum = s.RequestsThisSession, AskedToRest = s.RestRequests,
            Believed = s.Profile, Truth = result.User, EdgeChecks = checks.Count,
            Miss = checks.Count > 0 ? checks.Average(Off) : 0,
            LateMiss = checks.Count > 0 ? checks.Skip(checks.Count / 2).Average(Off) : 0,
            LowestControl = b.LowestControl, LowestControlTrue = result.Truth?.LowestControl, ControlGap = gap,
            PeakNeed = b.PeakNeed, PeakOverwhelm = b.PeakOverwhelm, Purposes = purposes,
            Aims = plan?.Aims.Count(x => x.Started) ?? 0, Segments = started.Count, Written = started.Count(x => x.Written),
        };
        return m with { Flags = Flags(result, m) };
    }

    /// <summary>Anything in a session that suggests the tuning is off. An empty list is a session that went as designed.</summary>
    private static List<string> Flags(SimulationResult result, SessionMetrics m)
    {
        var s = result.State;
        var flags = new List<string>();
        var steps = result.Steps;
        bool guided = s.Plan is not null && !s.Contract.GamesOnly;
        if (s.Active) flags.Add("the session did not end");

        // Length against what his setting means, unless something ended it on purpose.
        if (guided && m.TargetMinutes > 0 && m.EndedEarly.Length == 0)
        {
            double ratio = m.Minutes / m.TargetMinutes;
            if (ratio < 0.75) flags.Add($"ran short: {m.Minutes:0} of {m.TargetMinutes:0} minutes");
            else if (ratio > 1.3) flags.Add($"ran long: {m.Minutes:0} of {m.TargetMinutes:0} minutes");
        }

        // Slips: one she saw coming is his to answer for; one she did not is her estimate being wrong.
        foreach (var line in result.Lines.Where(x => x.Who == "Him" && x.Text.Contains("without permission")))
        {
            var at = steps.LastOrDefault(x => x.Start < line.At);
            if (at is null) continue;
            double believed = at.EstimateEnd.Control;
            flags.Add(believed > 0.3
                ? $"slip at {Clock(line.At)} she did not see coming: she had his control at {Pct(believed)}"
                : $"slip at {Clock(line.At)} (she had his control at {Pct(believed)})");
        }

        // Her model of him.
        if (m.EdgeChecks >= 6 && m.LateMiss > 0.35) flags.Add($"her edge timing is still off by {Pct(m.LateMiss)} late in the session");
        if (m.Truth is { } truth && m.EdgeChecks >= 6)
        {
            if (Math.Abs(m.Believed.Pace - truth.Pace) / truth.Pace > 0.2) flags.Add($"pace not learned: she has {m.Believed.Pace:0.00}, true {truth.Pace:0.00}");
            if (m.Believed.RecoveryEvidence >= 4 && Math.Abs(m.Believed.Recovery - truth.Recovery) / truth.Recovery > 0.35)
                flags.Add($"recovery not learned: she has {m.Believed.Recovery:0}s, true {truth.Recovery:0}s");
        }
        // She is meant to err on the careful side; thinking he has more left than he does is what leads to slips.
        // (Only where the edges are hers: when they are his she is not steering by it.)
        bool steers = guided && s.Contract.Edges == Authority.Domme;
        if (result.Truth is not null && steers && m.ControlGap > 10) flags.Add($"she overestimates his control by {m.ControlGap:0} points on average");
        if (result.Truth is not null && steers && m.ControlGap < -30) flags.Add($"she underestimates his control by {-m.ControlGap:0} points on average: far too careful");

        // The shape of the session.
        foreach (var step in steps.Where(x => x.Directive.Kind == Data.InstructionKind.EdgeCount && x.Directive.DurationOrCount == 1 && (x.End - x.Start).TotalSeconds > 180 && x.Game is null))
            flags.Add($"one edge took {Span(step.End - step.Start)} at {Clock(step.Start)} ({Simulation.Describe(step)})");
        if (guided && result.Truth is { LongestAbove80: > 600 } t) flags.Add($"{Span(t.LongestAbove80)} without coming below 80%");
        if (guided && m.Minutes >= 20 && m.PeakNeed < 0.3) flags.Add($"need only reached {Pct(m.PeakNeed)}");
        if (guided && m.Minutes >= 25 && s.Contract.Edges == Authority.Domme && m.LowestControl > 0.8 && m.EndedEarly.Length == 0)
            flags.Add($"his control was never tested (lowest {Pct(m.LowestControl)})");
        var started = s.Plan?.Segments.Where(x => x.Started && x.Type != SegmentCatalogue.Ending).ToList() ?? [];
        for (int i = 3; i < started.Count; i++)
            if (started[i].Type == started[i - 1].Type && started[i].Type == started[i - 2].Type && started[i].Type == started[i - 3].Type)
            {
                flags.Add($"the same segment four times running ({SegmentCatalogue.Default.Find(started[i].Type)?.Name ?? started[i].Type})");
                break;
            }
        var top = m.Purposes.FirstOrDefault();
        double total = m.Purposes.Sum(x => x.Seconds);
        if (guided && m.Minutes >= 25 && total > 0 && top.Seconds / total > 0.75) flags.Add($"{Pct(top.Seconds / total)} of the session was one purpose ({Short(top.Purpose)})");
        return flags;
    }

    // ── One session ──

    /// <summary>
    /// A session as it would be read: the settings it started from, what it came to, the methods
    /// used, the flow (aims, segments, steps) and the chat. <paramref name="detail"/> adds what is
    /// only useful for debugging her model against the simulated user (states, estimates, flags).
    /// </summary>
    public static string Transcript(SimulationResult result, string title, bool detail = false)
    {
        var s = result.State;
        var m = Measure(result, title);
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine("```");
        Overview(sb, result, m);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Flow");
        sb.AppendLine("```");
        Flow(sb, result);
        sb.AppendLine("```");
        if (result.Revisions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Changes of plan:");
            foreach (var revision in result.Revisions) sb.AppendLine($"- {revision}");
        }
        sb.AppendLine();
        sb.AppendLine("## Chat");
        sb.AppendLine("```");
        string? segment = null;
        foreach (var line in result.Lines)
        {
            if (line.Segment is { } seg && seg != segment && line.Who.StartsWith("Her"))
            {
                segment = seg;
                sb.AppendLine($"        -- {SegmentCatalogue.Default.Find(seg)?.Name ?? seg} --");
            }
            string who = line.Who switch { "Her (aside)" => "her~", "Her" => "HER ", "Him" => "him ", _ => "    " };
            sb.AppendLine($"{Clock(line.At)}  {who} {(line.Who == "Note" ? $"({line.Text})" : line.Text)}");
        }
        sb.AppendLine("```");
        if (!detail) return sb.ToString();

        sb.AppendLine();
        sb.AppendLine("## Model detail (simulated user: only for debugging her estimates)");
        sb.AppendLine("```");
        Glance(sb, result, m);
        sb.AppendLine("```");
        if (s.Plan is { Aims.Count: > 0 })
        {
            sb.AppendLine("One line per segment: her estimate of his closeness, need, control and overwhelm when it ended.");
            sb.AppendLine("```");
            Timeline(sb, result);
            sb.AppendLine("```");
        }
        if (result.Steps.Count > 0)
        {
            sb.AppendLine("Every step. States are start>end; est is her estimate, true the simulated user. Gap is the hands-off time just before.");
            sb.AppendLine("```");
            StepTable(sb, result);
            sb.AppendLine("```");
        }
        if (s.Body.Checks.Count > 0)
        {
            sb.AppendLine("Each edge where she set the method and pace: how long she expected it to take, and how long it took.");
            sb.AppendLine("```");
            sb.AppendLine($"{"At",-6}{"Method",-28}{"From",6}{"Rest",7}{"Expected",10}{"Actual",8}{"Off",7}");
            foreach (var c in s.Body.Checks)
                sb.AppendLine($"{Clock(TimeSpan.FromSeconds(c.AtSeconds)),-6}{Fit(c.Method, 27),-28}{Pct(c.ClosenessBefore),6}{(c.RestBefore >= 0 ? Span(c.RestBefore) : "-"),7}" +
                    $"{(c.ExpectedSeconds >= Body.Unreachable ? "never" : $"{c.ExpectedSeconds:0}s"),10}{$"{c.ActualSeconds:0}s",8}" +
                    $"{(c.ExpectedSeconds >= Body.Unreachable ? "" : $"{(c.ActualSeconds - c.ExpectedSeconds) / Math.Max(1, c.ExpectedSeconds):+0%;-0%}"),7}");
            sb.AppendLine("```");
        }
        return sb.ToString();
    }

    private static bool HandsOff(StepRecord step)
        => step.Game is null && step.Directive.Category != StepCategory.Hold
            && (step.Directive.Method is null || step.Directive.Category is StepCategory.Rest or StepCategory.Offer);

    /// <summary>The settings the session started from, what it came to, and the methods she used.</summary>
    private static void Overview(StringBuilder sb, SimulationResult result, SessionMetrics m)
    {
        var s = result.State;
        void Line(string label, string text) => sb.AppendLine($"{label,-10}{text}");
        Line("Settings", $"{s.Contract.Mode}{(s.Contract.Punishment ? ", punishment session" : "")}; intensity {s.Intensity}, length {s.Length}" +
            $"{(m.TargetMinutes > 0 ? $" (about {m.TargetMinutes:0} min)" : "")}, denial {s.Denial}, meanness {s.Mean}");
        Line("Her", $"harshness {s.Persona.Harshness}, humiliation {s.Persona.Humiliation}, mind games {s.Persona.Manipulation}, talkativeness {s.Persona.Talkativeness}; " +
            $"leaned toward {s.Ending.Kind} ({s.Ending.Reason})");
        if (result.User is { } u) Line("User", $"simulated \"{u.Name}\", seed {result.Seed}");
        sb.AppendLine();
        Line("Result", $"{m.Outcome} after {Span(m.Minutes * 60)}; {m.Edges} edge{(m.Edges == 1 ? "" : "s")}" +
            $"{(m.Slips > 0 ? $", came without permission {m.Slips}x" : "")}; asked to cum {m.AskedToCum}x, to rest {m.AskedToRest}x" +
            (m.EndedEarly.Length > 0 ? $"; ended early: {m.EndedEarly}" : ""));

        TimeSpan stroking = TimeSpan.Zero, handsOff = TimeSpan.Zero, holds = TimeSpan.Zero, games = TimeSpan.Zero;
        var methods = new Dictionary<string, (TimeSpan Time, int Edges, int Steps)>();
        foreach (var step in result.Steps)
        {
            var took = step.End - step.Start;
            var d = step.Directive;
            if (step.Game is not null) games += took;
            else if (d.Category == StepCategory.Hold) holds += took;
            else if (HandsOff(step)) handsOff += took;
            else stroking += took;
            if (d.Method is null || HandsOff(step) || d.Category == StepCategory.Hold) continue;
            string name = d.Method.IsUserChoice ? "(his choice of grip)" : d.Method.Name;
            var was = methods.GetValueOrDefault(name);
            methods[name] = (was.Time + took, was.Edges + step.Edges, was.Steps + 1);
        }
        Line("Time", $"stroking {Span(stroking)}, hands off {Span(handsOff)}, holding at the edge {Span(holds)}, games {Span(games)}");
        if (m.Purposes.Count > 0)
        {
            double total = m.Purposes.Sum(x => x.Seconds);
            Line("Purposes", string.Join(", ", m.Purposes.Select(x => $"{Short(x.Purpose)} {Pct(x.Seconds / total)}")) + $"  ({m.Aims} aims, {m.Segments} segments, {m.Written} written)");
        }
        if (methods.Count == 0) return;
        sb.AppendLine();
        sb.AppendLine($"{"Methods",-34}{"Time",7}{"Times",7}{"Edges",7}");
        foreach (var (name, x) in methods.OrderByDescending(x => x.Value.Time))
            sb.AppendLine($"  {Fit(name, 31),-32}{Span(x.Time),7}{x.Steps,7}{(x.Edges > 0 ? x.Edges.ToString() : ""),7}");
    }

    /// <summary>The session as it went: her aims, the segments inside each, and every step with its method, speed, time and edges.</summary>
    private static void Flow(StringBuilder sb, SimulationResult result)
    {
        var s = result.State;
        var plan = s.Plan;
        TimeSpan At(DateTime? t) => (t ?? s.LastActivityUtc) - s.StartedAtUtc;
        var segments = plan?.Segments.Where(x => x.Started && x.Type != SegmentCatalogue.Ending).ToList() ?? [];
        PlannedSegment? current = null;
        int aim = -1;
        bool ending = false;
        foreach (var step in result.Steps)
        {
            var planned = step.Directive.Category == StepCategory.Finish ? null
                : segments.LastOrDefault(x => x.StartedUtc is DateTime since && since - s.StartedAtUtc <= step.Start);
            if (planned is not null && planned != current)
            {
                current = planned;
                if (planned.Aim != aim && planned.Aim >= 0 && planned.Aim < plan!.Aims.Count)
                {
                    if (aim >= 0) sb.AppendLine();
                    aim = planned.Aim;
                    var a = plan.Aims[aim];
                    string written = SegmentCatalogue.Default.FindAim(a.Written) is { } shape ? $" as \"{shape.Name}\"" : "";
                    sb.AppendLine($"AIM {aim + 1}: {Purposes.Name(a.Purpose).ToUpperInvariant()}{written}{(a.Origin == "plan" ? "" : $" [{a.Origin}]")}   {Clock(At(a.StartedUtc))}-{Clock(At(a.EndedUtc))}");
                    sb.AppendLine($"       she wanted: {a.Goal}{(a.Ended.Length > 0 ? $"; ended because {a.Ended}" : "")}");
                }
                var inside = result.Steps.Where(x => x.Start >= At(planned.StartedUtc) && x.Start < At(planned.EndedUtc)).ToList();
                int edges = inside.Sum(x => x.Edges);
                string role = planned.Role switch { SegmentRole.Advancing => "", SegmentRole.Supporting => ", supporting", _ => ", on the side" };
                string name = SegmentCatalogue.Default.Find(planned.Type)?.Name ?? planned.Type;
                sb.AppendLine($"  {name}{(planned.Written ? " (written)" : "")}{role}: {Span(At(planned.EndedUtc) - At(planned.StartedUtc))}" +
                    $"{(edges > 0 ? $", {edges} edge{(edges == 1 ? "" : "s")}" : "")}{(planned.Origin == "plan" ? "" : $"   [{planned.Origin}]")}");
            }
            else if (step.Directive.Category == StepCategory.Finish && !ending)
            {
                ending = true;
                current = null;
                sb.AppendLine();
                sb.AppendLine("THE ENDING");
            }
            var d = step.Directive;
            // An edge step says its own count; anywhere else an edge is worth noting.
            string noted = step.Edges > 0 && d.Kind != Data.InstructionKind.EdgeCount ? $"   <- {step.Edges} edge{(step.Edges == 1 ? "" : "s")}" : "";
            sb.AppendLine($"    {Clock(step.Start)} {Span(step.End - step.Start),5}  {Simulation.Describe(step)}{noted}");
        }
    }

    private static void Glance(StringBuilder sb, SimulationResult result, SessionMetrics m)
    {
        var s = result.State;
        var b = s.Body;
        var p = s.Profile;
        void Line(string label, string text) => sb.AppendLine($"{label,-11}{text}");

        Line("Session", $"{s.Contract.Mode}{(s.Contract.Punishment ? ", punishment" : "")}; intensity {s.Intensity}, length {s.Length}, denial {s.Denial}, meanness {s.Mean}; " +
            $"persona harsh {s.Persona.Harshness}, humiliation {s.Persona.Humiliation}, mind games {s.Persona.Manipulation}, talk {s.Persona.Talkativeness}");
        if (result.User is { } u) Line("User", $"\"{u.Name}\" (true pace {u.Pace:0.00}, recovery {u.Recovery:0}s, tolerance {u.Tolerance:0}s), seed {result.Seed}");
        sb.AppendLine();
        Line("FLAGS", m.Flags.Count == 0 ? "none" : $"{m.Flags.Count}");
        foreach (var flag in m.Flags) sb.AppendLine($"  ! {flag}");
        sb.AppendLine();
        Line("Outcome", $"{m.Outcome}; {m.Edges} edges, {m.Slips} slip{(m.Slips == 1 ? "" : "s")}; asked to cum {m.AskedToCum}, to rest {m.AskedToRest}");
        Line("Ending", $"she leaned toward {s.Ending.Kind} ({s.Ending.Reason})");
        if (m.TargetMinutes > 0)
            Line("Length", $"{Span(m.Minutes * 60)} of {m.TargetMinutes:0} min ({Pct(m.Minutes / m.TargetMinutes)}){(m.EndedEarly.Length > 0 ? $"; ended early: {m.EndedEarly}" : "")}");
        if (m.Purposes.Count > 0)
        {
            double total = m.Purposes.Sum(x => x.Seconds);
            Line("Purposes", string.Join(", ", m.Purposes.Select(x => $"{Short(x.Purpose)} {Pct(x.Seconds / total)}")) + $"   ({m.Aims} aims, {m.Segments} segments, {m.Written} written)");
        }
        string Bands(BodyState x) => x.TotalSeconds <= 0 ? "" :
            $"under 50: {Pct(x.SecondsBelow50 / x.TotalSeconds)}, 50-75: {Pct(x.Seconds50To75 / x.TotalSeconds)}, 75-90: {Pct(x.Seconds75To90 / x.TotalSeconds)}, over 90: {Pct(x.SecondsAbove90 / x.TotalSeconds)}";
        Line("Closeness", Bands(b) + (result.Truth is { } t ? $"   (true: {Bands(t)})" : ""));
        Line("Need", $"built to {Pct(b.PeakNeed)}");
        Line("Control", $"lowest {Pct(b.LowestControl)}{(result.Truth is { } t2 ? $" (true {Pct(t2.LowestControl)})" : "")}; under 35% for {Span(b.SecondsNearLimit)}" +
            (result.Truth is not null ? $"; her estimate runs {Math.Abs(m.ControlGap):0} points {(m.ControlGap <= 0 ? "under" : "OVER")} the truth on average" : ""));
        Line("Overwhelm", $"peak {Pct(b.PeakOverwhelm)}");
        string True(double? v, string format) => v is double x ? $" (true {x.ToString(format)})" : "";
        Line("Her model", $"pace {p.Pace:0.00}{True(result.User?.Pace, "0.00")}, recovery {p.Recovery:0}s{True(result.User?.Recovery, "0")}, tolerance {p.Tolerance:0}s{True(result.User?.Tolerance, "0")}" +
            (m.EdgeChecks > 0 ? $"; edge timing off by {Pct(m.Miss)} over {m.EdgeChecks} edges, {Pct(m.LateMiss)} in the later half" : ""));
        Line("", $"sources: pace {p.PaceSource}; recovery {p.RecoverySource}; tolerance {p.ToleranceSource}");
        var e = s.Earned;
        double other = s.Points - (e.Stroking + e.Close + e.Torture + e.Edges + e.Holds);
        Line("Points", $"{s.Points:0} of {s.Budget:0}: stroking {e.Stroking:0}, torture {e.Torture:0}, closeness {e.Close:+0;-0;0}, edges {e.Edges:0}, holds {e.Holds:0}, games and deals {other:+0;-0;0}");
    }

    private static void Timeline(StringBuilder sb, SimulationResult result)
    {
        var s = result.State;
        var plan = s.Plan!;
        TimeSpan At(DateTime? t) => (t ?? s.LastActivityUtc) - s.StartedAtUtc;
        for (int i = 0; i < plan.Aims.Count; i++)
        {
            var aim = plan.Aims[i];
            string written = SegmentCatalogue.Default.FindAim(aim.Written) is { } shape ? $" as \"{shape.Name}\"" : "";
            string origin = aim.Origin == "plan" ? "" : $" [{aim.Origin}]";
            if (!aim.Started)
            {
                sb.AppendLine($"AIM {i + 1}  {Purposes.Name(aim.Purpose)}{written}{origin}: never begun");
                continue;
            }
            sb.AppendLine();
            sb.AppendLine($"AIM {i + 1}  {Purposes.Name(aim.Purpose).ToUpperInvariant()}{written}{origin}  {Clock(At(aim.StartedUtc))}-{Clock(At(aim.EndedUtc))}  wanted: {aim.Goal}{(aim.Achieved ? " (reached)" : "")}");
            sb.AppendLine($"       why: {aim.Why}{(aim.Ended.Length > 0 ? $"   ended: {aim.Ended}" : "")}");
            foreach (var seg in plan.Segments.Where(x => x.Aim == i && x.Started && x.Type != SegmentCatalogue.Ending))
            {
                var start = At(seg.StartedUtc);
                var end = At(seg.EndedUtc);
                var inside = result.Steps.Where(x => x.Start >= start && x.Start < end).ToList();
                var last = inside.LastOrDefault();
                string states = last is null ? "" : $"C{last.EstimateEnd.Closeness * 100,3:0} N{last.EstimateEnd.Need * 100,3:0} Ct{last.EstimateEnd.Control * 100,3:0} O{last.EstimateEnd.Overwhelm * 100,3:0}";
                int edges = inside.Sum(x => x.Edges);
                string role = seg.Role switch { SegmentRole.Advancing => "", SegmentRole.Supporting => " (support)", _ => " (side)" };
                string name = $"{SegmentCatalogue.Default.Find(seg.Type)?.Name ?? seg.Type}{(seg.Written ? "*" : "")}{role}";
                string ended = seg.Ended.Length > 0 && !seg.Ended.StartsWith("ran its") && !seg.Ended.StartsWith("the aim ended") ? $" -> {seg.Ended}" : "";
                sb.AppendLine($"  {Clock(start)} {Span(end - start),5}  {Fit(name, 30)} {Fit(Short(seg.Purpose), 9)} {(edges > 0 ? $"{edges}e" : ""),3}  {states,-22} {seg.Reason}{ended}");
            }
        }
    }

    private static void StepTable(StringBuilder sb, SimulationResult result)
    {
        bool truth = result.Truth is not null;
        sb.AppendLine($"{"At",-6}{"Took",-6}{"What she had him do",-58}{"Edg",-4}{"Gap",-6}{"Close est",-10}{(truth ? "true" : ""),-8}{"Need",-7}{"Ctl est",-9}{(truth ? "true" : ""),-8}{"Overwh",-7}");
        string? segment = null;
        var s = result.State;
        var segments = s.Plan?.Segments.Where(x => x.Started && x.Type != SegmentCatalogue.Ending).ToList() ?? [];
        PlannedSegment? current = null;
        var gap = TimeSpan.Zero;
        foreach (var step in result.Steps)
        {
            var d = step.Directive;
            var took = step.End - step.Start;
            var planned = segments.LastOrDefault(x => x.StartedUtc is DateTime since && since - s.StartedAtUtc <= step.Start);
            if (planned is not null && planned != current)
            {
                current = planned;
                sb.AppendLine($"  -- {SegmentCatalogue.Default.Find(planned.Type)?.Name ?? planned.Type}: {planned.Parameters()}");
            }
            else if (planned is null && step.Segment is { } id && id != segment)
            {
                segment = id;
                sb.AppendLine($"  -- {SegmentCatalogue.Default.Find(id)?.Name ?? id}");
            }
            bool handsOff = step.Game is null && d.Category != StepCategory.Hold && (d.Method is null || d.Category is StepCategory.Rest or StepCategory.Offer);
            sb.AppendLine($"{Clock(step.Start),-6}{Span(took),-6}{Fit(Simulation.Describe(step), 57),-58}{(step.Edges > 0 ? step.Edges.ToString() : ""),-4}{(!handsOff && gap > TimeSpan.Zero ? Span(gap) : ""),-6}" +
                $"{Pair(step.EstimateStart.Closeness, step.EstimateEnd.Closeness),-10}{(truth ? Pair(step.TruthStart.Closeness, step.TruthEnd.Closeness) : ""),-8}" +
                $"{Pair(step.EstimateStart.Need, step.EstimateEnd.Need),-7}{Pair(step.EstimateStart.Control, step.EstimateEnd.Control),-9}" +
                $"{(truth ? Pair(step.TruthStart.Control, step.TruthEnd.Control) : ""),-8}{Pair(step.EstimateStart.Overwhelm, step.EstimateEnd.Overwhelm),-7}");
            gap = handsOff ? gap + took : TimeSpan.Zero;
        }
    }

    // ── A whole run on one page ──

    /// <summary>
    /// A run of sessions on one page: what each started from and came to. <paramref name="detail"/>
    /// adds how her model did against the simulated users, and the flags.
    /// </summary>
    public static string Summary(IReadOnlyList<SessionMetrics> runs, string? settings = null, bool detail = false)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Simulated sessions");
        sb.AppendLine();
        if (settings is not null) sb.AppendLine($"_{settings}_").AppendLine();
        var guided = runs.Where(x => x.TargetMinutes > 0 && x.Segments > 0).ToList();
        var full = guided.Where(x => x.EndedEarly.Length == 0).ToList();
        sb.AppendLine("```");
        sb.AppendLine($"Sessions   {runs.Count}");
        sb.AppendLine($"Outcomes   {string.Join(", ", runs.GroupBy(x => x.Outcome).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}"))}");
        if (full.Count > 0)
            sb.AppendLine($"Length     {Pct(full.Average(x => x.Minutes / x.TargetMinutes))} of what the Length setting means on average ({Pct(full.Min(x => x.Minutes / x.TargetMinutes))} to {Pct(full.Max(x => x.Minutes / x.TargetMinutes))}); " +
                $"{guided.Count - full.Count} more ended early (marked *)");
        if (guided.Count > 0)
        {
            double total = guided.Sum(x => x.Purposes.Sum(p => p.Seconds));
            var mix = guided.SelectMany(x => x.Purposes).GroupBy(x => x.Purpose).Select(g => (g.Key, g.Sum(x => x.Seconds))).OrderByDescending(x => x.Item2);
            sb.AppendLine($"Purposes   {string.Join(", ", mix.Select(x => $"{Short(x.Key)} {Pct(x.Item2 / total)}"))}; {Pct(guided.Sum(x => x.Written) / (double)Math.Max(1, guided.Sum(x => x.Segments)))} of segments written");
        }
        sb.AppendLine();
        sb.AppendLine("Sliders: intensity/length/denial/meanness. Minutes: run / what the Length means (* ended early: a deal, permission or a slip).");
        sb.AppendLine("Mix: share of time on ache/control/overwhelm/starve.");
        sb.AppendLine();
        sb.AppendLine($"{"Session",-34}{"Sliders",-10}{"Outcome",-20}{"Minutes",-9}{"Edges",-7}{"Aims",-6}{"Segs",-6}{"Mix a/c/o/s",-13}");
        foreach (var x in runs)
        {
            double total = Math.Max(1, x.Purposes.Sum(p => p.Seconds));
            double Share(Purpose p) => x.Purposes.Where(y => y.Purpose == p).Sum(y => y.Seconds) / total * 100;
            string mix = x.Segments == 0 ? "-" : $"{Share(Purpose.Ache):0}/{Share(Purpose.Control):0}/{Share(Purpose.Overwhelm):0}/{Share(Purpose.Starve):0}";
            sb.AppendLine($"{Fit(x.Name, 33),-34}{x.Sliders,-10}{Fit(x.Outcome, 19),-20}{(x.TargetMinutes > 0 ? $"{x.Minutes:0}/{x.TargetMinutes:0}{(x.EndedEarly.Length > 0 ? "*" : "")}" : $"{x.Minutes:0}"),-9}" +
                $"{x.Edges,-7}{(x.Aims > 0 ? x.Aims.ToString() : "-"),-6}{(x.Segments > 0 ? x.Segments.ToString() : "-"),-6}{mix,-13}");
        }
        sb.AppendLine("```");
        if (!detail) return sb.ToString();

        var flagged = runs.Where(x => x.Flags.Count > 0).ToList();
        var learned = runs.Where(x => x.Truth is not null && x.EdgeChecks >= 6).ToList();
        sb.AppendLine();
        sb.AppendLine("## Model detail (simulated users: only for debugging her estimates)");
        sb.AppendLine("```");
        sb.AppendLine($"Slips      {runs.Count(x => x.Slips > 0)} sessions; by user: {string.Join(", ", runs.GroupBy(x => x.User).Select(g => $"{g.Key} {g.Count(x => x.Slips > 0)}/{g.Count()}"))}");
        if (learned.Count > 0)
            sb.AppendLine($"Her model  pace off by {Pct(learned.Average(x => Math.Abs(x.Believed.Pace - x.Truth!.Pace) / x.Truth.Pace))}, recovery by {Pct(learned.Average(x => Math.Abs(x.Believed.Recovery - x.Truth!.Recovery) / x.Truth.Recovery))}; " +
                $"edge timing off by {Pct(learned.Average(x => x.LateMiss))} in the later half; control estimate {learned.Average(x => x.ControlGap):+0;-0;0} points against the truth ({learned.Count} sessions with 6 or more measured edges)");
        sb.AppendLine();
        sb.AppendLine($"{"Session",-34}{"Slip",-5}{"Pace",-11}{"Rec",-8}{"Miss",-6}{"Ctl",-9}{"Need",-6}{"Ovw",-6}{"Flags",-5}");
        foreach (var x in runs)
            sb.AppendLine($"{Fit(x.Name, 33),-34}{(x.Slips > 0 ? x.Slips.ToString() : ""),-5}" +
                $"{(x.Truth is { } t ? $"{x.Believed.Pace:0.00}/{t.Pace:0.00}" : $"{x.Believed.Pace:0.00}"),-11}{(x.Truth is { } t2 ? $"{x.Believed.Recovery:0}/{t2.Recovery:0}" : $"{x.Believed.Recovery:0}"),-8}" +
                $"{(x.EdgeChecks > 0 ? Pct(x.LateMiss) : "-"),-6}{$"{x.LowestControl * 100:0}/{x.LowestControlTrue * 100:0}",-9}{Pct(x.PeakNeed),-6}{Pct(x.PeakOverwhelm),-6}{(x.Flags.Count > 0 ? x.Flags.Count.ToString() : ""),-5}");
        sb.AppendLine();
        foreach (var run in flagged)
        {
            sb.AppendLine(run.Name);
            foreach (var flag in run.Flags) sb.AppendLine($"  ! {flag}");
        }
        sb.AppendLine("```");
        return sb.ToString();
    }
}
