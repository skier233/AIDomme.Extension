using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIDome.Sessions.Plan;

/// <summary>
/// What she wants him to feel. Every segment has exactly one; an aim is a purpose pursued across
/// several segments.
/// </summary>
public enum Purpose
{
    /// <summary>Build the ache: keep him wanting, his need climbing.</summary>
    Ache,
    /// <summary>Test control: at and near the edge, wearing down his margin before a slip.</summary>
    Control,
    /// <summary>Overwhelm: stimulation he would rather stop. Torture, never pain.</summary>
    Overwhelm,
    /// <summary>Starve: too little touch, or none while he watches and listens; desperate for more.</summary>
    Starve,
    /// <summary>Cool: bring him down. A real break.</summary>
    Cool,
}

/// <summary>What a segment is doing inside the aim it belongs to.</summary>
public enum SegmentRole
{
    /// <summary>The aim's own purpose.</summary>
    Advancing,
    /// <summary>There so the aim can continue or bite harder: a break, a game with the aim's stakes.</summary>
    Supporting,
    /// <summary>Something else for a moment, because it suits her: torment in the middle of edging.</summary>
    Side,
}

public static class Purposes
{
    public static string Name(Purpose p) => p switch
    {
        Purpose.Ache => "Build the ache", Purpose.Control => "Test control", Purpose.Overwhelm => "Overwhelm",
        Purpose.Starve => "Starve", _ => "Cool",
    };

    public static string Feel(Purpose p) => p switch
    {
        Purpose.Ache => "Wanting it more and more: close for a long time, never finished.",
        Purpose.Control => "At the edge and struggling to stay there: how much can he take before he slips.",
        Purpose.Overwhelm => "Stimulation so much he is desperate for it to stop.",
        Purpose.Starve => "Almost nothing, or nothing at all: desperate for more.",
        _ => "A real break, so he can take more.",
    };

    public static string Id(Purpose p) => p.ToString().ToLowerInvariant();
}

/// <summary>What one step of a written segment asks for.</summary>
public enum StepKind
{
    /// <summary>A method for a time.</summary>
    Timed,
    /// <summary>A number of edges, optionally holding the last.</summary>
    Edges,
    /// <summary>One edge, held for a long time.</summary>
    Hold,
    /// <summary>A method for a time without reaching the edge. An edge fails it.</summary>
    NoEdge,
    /// <summary>As many edges as he can in the time.</summary>
    MaxEdges,
    /// <summary>Hands off.</summary>
    Rest,
    /// <summary>Hands off while she talks: a rest that is not a rest.</summary>
    TeaseRest,
    /// <summary>A game with stakes.</summary>
    Game,
    /// <summary>A short burst at full speed.</summary>
    Burst,
}

/// <summary>How a written segment picks its methods.</summary>
public sealed record MethodPolicy
{
    /// <summary>
    /// <c>same</c>: one method for the whole segment; <c>rotate</c>: varies, may stay on a good grip;
    /// <c>swap</c>: a different method every step; <c>favorite</c>: what he edges on most;
    /// <c>hated</c>: the most torturous available.
    /// </summary>
    public string Policy { get; init; } = "rotate";
    /// <summary><c>tease</c>, <c>torture</c>, <c>closeness</c>, <c>basic</c> or <c>any</c>.</summary>
    public string Prefer { get; init; } = "basic";
    /// <summary>The hardest method allowed, relative to the session's intensity preference.</summary>
    public int Ceiling { get; init; }
}

/// <summary>The shape of the steps a written segment is made of. Ranges are [min, max].</summary>
public sealed record StepTemplate
{
    public StepKind Kind { get; init; }
    public int[] Speed { get; init; } = [4, 6];
    /// <summary>Speed climbs from the low end to the high end across the segment's steps.</summary>
    public bool Rise { get; init; }
    public int[] Seconds { get; init; } = [45, 90];
    public int[] Count { get; init; } = [1, 2];
    public int[] Hold { get; init; } = [0, 0];
    public double HoldChance { get; init; }
    public MethodPolicy Method { get; init; } = new();
    /// <summary>
    /// For steps that ask for edges: "" (a method that gets him there) or "hard" (on purpose, an
    /// intense grip that barely does: slow, overwhelming, and he has to earn it).
    /// </summary>
    public string Reach { get; init; } = "";
}

/// <summary>
/// A name for a segment. Most are labels for the shapes she improvises (the steerer makes their
/// steps from his state; the entry only gives the shape a name, a goal and tags for her lines).
/// A <see cref="Written"/> one has a specific shape that is the point: a ladder, a countdown, a
/// game, a false finish. Its steps come from <see cref="Step"/>.
/// </summary>
public sealed record SegmentType
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>What she is trying to make him do or feel.</summary>
    public string Goal { get; init; } = "";
    public string[] Tags { get; init; } = [];
    public Purpose Purpose { get; init; }
    public bool Written { get; init; }
    /// <summary>What she must decide for this to work: <c>pacing</c>, <c>edges</c>, <c>methods</c>, <c>games</c>.</summary>
    public string[] Needs { get; init; } = [];
    /// <summary>Written: share of the session's points budget, [min, max].</summary>
    public double[] Share { get; init; } = [0.04, 0.1];
    /// <summary>Written: steps, [min, max].</summary>
    public int[] Steps { get; init; } = [1, 8];
    /// <summary>Written: the aims it may appear in (purposes). Empty: any.</summary>
    public Purpose[] Fits { get; init; } = [];
    /// <summary>Written: chance of a short rest between its steps.</summary>
    public double RestBetween { get; init; }
    /// <summary>Minimum persona dials for her to use it (for example <c>manipulation</c>: 4).</summary>
    public Dictionary<string, int> Persona { get; init; } = [];
    /// <summary>False for what she never chooses herself (the ending, a label only events produce).</summary>
    public bool Planned { get; init; } = true;
    /// <summary>Written: how often its turn comes up, relative to the others.</summary>
    public double Weight { get; init; } = 1;
    public StepTemplate Step { get; init; } = new();

    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    public bool HasAny(IEnumerable<string> tags) => tags.Any(Has);
}

/// <summary>
/// A written aim: a shape across several segments that is the point (back and forth between
/// overwhelm and intense edging; dread; mercy with a catch). The pattern is the purposes of its
/// segments in order; it repeats until the aim's share is spent.
/// </summary>
public sealed record WrittenAim
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Goal { get; init; } = "";
    public Purpose Purpose { get; init; }
    public Purpose[] Pattern { get; init; } = [];
    /// <summary>Seconds each segment of the pattern runs, [min, max].</summary>
    public int[] Seconds { get; init; } = [60, 100];
    public string[] Needs { get; init; } = [];
    public Dictionary<string, int> Persona { get; init; } = [];
    /// <summary>Chance (0 to 1) she takes this shape when an aim of its purpose begins and it is fresh.</summary>
    public double Chance { get; init; } = 0.2;
    /// <summary>Set when only an event brings it on: <c>restbeg</c>.</summary>
    public string Event { get; init; } = "";
}

public sealed record SegmentCatalogueFile(SegmentType[] Segments, WrittenAim[]? Aims, Dictionary<string, Dictionary<string, double>> MoodLenses,
    Dictionary<string, Purpose[]>? Requires = null);

/// <summary>The segment names and written content shipped with the extension, and how each mood leans on the purposes.</summary>
public sealed class SegmentCatalogue
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<SegmentCatalogue> Shipped = new(() =>
    {
        using var stream = typeof(SegmentCatalogue).Assembly.GetManifestResourceStream("AIDome.Plan.segments.json")
            ?? throw new InvalidOperationException("The segment catalogue is not embedded in the extension.");
        var file = JsonSerializer.Deserialize<SegmentCatalogueFile>(stream, Json) ?? throw new InvalidDataException("The segment catalogue is empty.");
        var catalogue = new SegmentCatalogue(file.Segments, file.Aims ?? [], file.MoodLenses ?? [], file.Requires);
        var problems = catalogue.Validate();
        if (problems.Count > 0) throw new InvalidDataException("The segment catalogue is invalid:\n" + string.Join("\n", problems));
        return catalogue;
    });
    public static SegmentCatalogue Default => Shipped.Value;

    public IReadOnlyList<SegmentType> Types { get; }
    public IReadOnlyList<WrittenAim> Aims { get; }
    /// <summary>How her mood leans on each purpose (and on <c>game</c>) when she chooses.</summary>
    public IReadOnlyDictionary<string, Dictionary<string, double>> MoodLenses { get; }
    /// <summary>Purposes a plan must include when the session asks for it (a punishment, a trait he owes).</summary>
    public IReadOnlyDictionary<string, Purpose[]> Requires { get; }

    /// <summary>The session conditions a requirement may name.</summary>
    public static readonly string[] RequirementKeys = ["punishment", "torturous", "teasing", "intense", "mean"];

    /// <summary>The names the steerer gives the shapes it improvises. Each must be in the catalogue.</summary>
    public static readonly string[] Shapes =
    [
        "slow-build", "stroke-steady", "keep-under", "edge-fresh", "edge-steady", "rapid-fire", "long-hold", "torment", "hated-grip", "punish",
        "tempo-crawl", "tease-rest", "breather",
    ];

    public SegmentCatalogue(IEnumerable<SegmentType> types, IEnumerable<WrittenAim> aims, IReadOnlyDictionary<string, Dictionary<string, double>> moodLenses,
        IReadOnlyDictionary<string, Purpose[]>? requires = null)
    {
        Types = types.ToList();
        Aims = aims.ToList();
        MoodLenses = moodLenses;
        Requires = requires ?? new Dictionary<string, Purpose[]>();
    }

    public SegmentType? Find(string id) => Types.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
    public WrittenAim? FindAim(string id) => Aims.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>How much a mood leans toward a purpose or a game (1 when it has no view).</summary>
    public double Lens(DommeMood mood, string key)
        => MoodLenses.TryGetValue(mood.ToString(), out var lens) && lens.TryGetValue(key, out var m) ? m : 1;

    /// <summary>The ending is always last; it is not chosen like the others.</summary>
    public const string Ending = "ending";

    public List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var dup in Types.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"Duplicate segment '{dup.Key}'.");
        if (Find(Ending) is null) problems.Add("The catalogue needs an 'ending' segment.");
        foreach (var shape in Shapes)
            if (Find(shape) is not { Written: false }) problems.Add($"The catalogue needs an improvised segment named '{shape}'.");
        foreach (var t in Types)
        {
            string at = $"'{t.Id}'";
            if (t.Name.Length == 0 || t.Goal.Length == 0) problems.Add($"{at}: needs a name and a goal.");
            foreach (var need in t.Needs)
                if (need is not ("pacing" or "edges" or "methods" or "games")) problems.Add($"{at}: unknown need '{need}'.");
            foreach (var key in t.Persona.Keys)
                if (key is not ("harsh" or "humiliation" or "manipulation" or "talk")) problems.Add($"{at}: unknown persona dial '{key}'.");
            if (!t.Written) continue;
            if (t.Share is not [var lo, var hi] || lo <= 0 || hi < lo || hi > 0.6) problems.Add($"{at}: share must be [min, max] within (0, 0.6].");
            if (t.Steps is not [var smin, var smax] || smin < 1 || smax < smin) problems.Add($"{at}: steps must be [min, max] from 1.");
            var s = t.Step;
            foreach (var range in new[] { s.Speed, s.Seconds, s.Count, s.Hold })
                if (range is not [var min, var max] || min < 0 || max < min) problems.Add($"{at}: a step range must be [min, max].");
            if (s.Method.Policy is not ("same" or "rotate" or "swap" or "favorite" or "hated")) problems.Add($"{at}: unknown method policy '{s.Method.Policy}'.");
            if (s.Method.Prefer is not ("tease" or "torture" or "closeness" or "basic" or "any")) problems.Add($"{at}: unknown method preference '{s.Method.Prefer}'.");
            if (s.Reach is not ("" or "hard")) problems.Add($"{at}: unknown reach '{s.Reach}'.");
            if (t.Weight <= 0) problems.Add($"{at}: weight must be positive.");
        }
        foreach (var dup in Aims.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"Duplicate aim '{dup.Key}'.");
        foreach (var aim in Aims)
        {
            string at = $"aim '{aim.Id}'";
            if (aim.Name.Length == 0 || aim.Goal.Length == 0) problems.Add($"{at}: needs a name and a goal.");
            if (aim.Pattern.Length == 0) problems.Add($"{at}: needs a pattern.");
            if (aim.Seconds is not [var min, var max] || min < 10 || max < min) problems.Add($"{at}: seconds must be [min, max] from 10.");
            if (aim.Event is not ("" or "restbeg")) problems.Add($"{at}: unknown event '{aim.Event}'.");
            foreach (var need in aim.Needs)
                if (need is not ("pacing" or "edges" or "methods" or "games")) problems.Add($"{at}: unknown need '{need}'.");
        }
        foreach (var key in Requires.Keys)
            if (!RequirementKeys.Contains(key)) problems.Add($"Unknown requirement '{key}'.");
        foreach (var (mood, lens) in MoodLenses)
        {
            if (!Enum.TryParse<DommeMood>(mood, true, out _)) problems.Add($"Mood lens for unknown mood '{mood}'.");
            foreach (var key in lens.Keys)
                if (key != "game" && !Enum.TryParse<Purpose>(key, true, out _)) problems.Add($"Mood lens '{mood}' refers to '{key}', which is neither a purpose nor 'game'.");
        }
        return problems;
    }
}

/// <summary>
/// What she is pursuing over a longer stretch: a purpose plus what she wants to have achieved. It
/// spans several segments, focuses them without excluding others, and survives breaks and games.
/// </summary>
public sealed class Aim
{
    public Purpose Purpose { get; set; }
    /// <summary>The written aim it follows, if any.</summary>
    public string Written { get; set; } = "";
    /// <summary>What she wants to have achieved, in words.</summary>
    public string Goal { get; set; } = "";
    /// <summary>The state she is after (need for Ache, control for Control, overwhelm for Overwhelm); 0 when it is only a matter of time.</summary>
    public double Target { get; set; }
    /// <summary>Why she chose it.</summary>
    public string Why { get; set; } = "";
    /// <summary>Why it ended; empty while it runs.</summary>
    public string Ended { get; set; } = "";
    /// <summary>Share of the session's points budget.</summary>
    public double Share { get; set; }
    public string Origin { get; set; } = "plan";
    public bool Started { get; set; }
    public bool Achieved { get; set; }
    public double PointsAtStart { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    /// <summary>Segments it has had, and where a written pattern has got to.</summary>
    public int Segments { get; set; }
    public int Phase { get; set; }
    /// <summary>The label its improvised segments take when an event asked for one (a punishment).</summary>
    public string Label { get; set; } = "";
}

/// <summary>One segment of a session: a few minutes with one purpose and the parameters she chose for it.</summary>
public sealed class PlannedSegment
{
    /// <summary>Its name in the catalogue: a written segment, or the label for the shape she improvised.</summary>
    public string Type { get; set; } = "";
    public Purpose Purpose { get; set; }
    public SegmentRole Role { get; set; }
    public bool Written { get; set; }
    /// <summary>One line: why this, now.</summary>
    public string Reason { get; set; } = "";
    /// <summary>Why it is in the plan: <c>plan</c>, or the event that put it there.</summary>
    public string Origin { get; set; } = "plan";
    /// <summary>The aim it belongs to (index into the plan's aims; -1 for the ending).</summary>
    public int Aim { get; set; } = -1;
    public bool Started { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }
    public string Ended { get; set; } = "";
    public int Steps { get; set; }
    public double PointsAtStart { get; set; }
    /// <summary>Set by an event: this segment ends at the next step.</summary>
    public bool Stop { get; set; }

    // Written segments run by steps and a share of the points.
    public double Share { get; set; }
    public int MinSteps { get; set; }
    public int MaxSteps { get; set; }
    /// <summary>The method fixed for the segment (policy <c>same</c> or <c>favorite</c>).</summary>
    public int? MethodId { get; set; }

    // Improvised segments run on the parameters she chose.
    /// <summary>How long she means it to last.</summary>
    public int Seconds { get; set; }
    /// <summary>How far she lets him come down before the next edge (closeness, 0 to 1). 1: no gap at all.</summary>
    public double Floor { get; set; }
    /// <summary>Whether its steps ask for edges (otherwise timed stroking).</summary>
    public bool Edges { get; set; }
    /// <summary>Timed stroking with an order not to reach the edge: an edge fails it.</summary>
    public bool KeepUnder { get; set; }
    /// <summary>The chance an edge is held, and how much of what he has left a hold spends.</summary>
    public double HoldChance { get; set; }
    public double HoldFraction { get; set; }
    /// <summary>Edges announced up front; 0 when the count is hidden ("edge", then "again").</summary>
    public int Announce { get; set; }
    public int SpeedLow { get; set; }
    public int SpeedHigh { get; set; }
    /// <summary>The methods she chose for it: all fit its purpose. Empty when the grip is his choice or hands are off.</summary>
    public List<int> MethodIds { get; set; } = [];
    public List<string> MethodNames { get; set; } = [];
    public int EdgesAsked { get; set; }
    public int? LastMethodId { get; set; }
    public int LastSpeed { get; set; }

    /// <summary>The parameters in a line, for the debug view and transcripts.</summary>
    public string Parameters()
    {
        if (Written) return $"{MinSteps} to {MaxSteps} steps, {Share:P0} of the points";
        var parts = new List<string> { $"about {Seconds / 60}:{Seconds % 60:00}" };
        if (Edges)
        {
            parts.Add(Floor >= 0.99 ? "no gaps" : $"back in at {Floor:P0}");
            parts.Add(HoldChance <= 0 ? "no holds" : $"holds on {HoldChance:P0} of edges, each {HoldFraction:P0} of what he has left");
            parts.Add(Announce > 0 ? $"{Announce} edges announced" : "count hidden");
        }
        if (KeepUnder) parts.Add("must stay away from the edge");
        if (SpeedHigh > 0) parts.Add(SpeedLow == SpeedHigh ? $"speed {SpeedLow}" : $"speeds {SpeedLow} to {SpeedHigh}");
        if (MethodNames.Count > 0) parts.Add("methods: " + string.Join(", ", MethodNames));
        return string.Join("; ", parts);
    }
}

/// <summary>
/// Her plan for the session: the aims she set at the start (revised as his state and events
/// demand) and the segments as they happened. Segments are chosen as she goes; anything after the
/// current one was queued by an event. Every choice records its reason.
/// </summary>
public sealed class SessionPlan
{
    public List<Aim> Aims { get; set; } = [];
    public int AimIndex { get; set; }
    public List<PlannedSegment> Segments { get; set; } = [];
    public int Index { get; set; }
    public List<string> Revisions { get; set; } = [];
    public int Foreshadowed { get; set; }
    /// <summary>How many times his begging has made the session longer.</summary>
    public int Pestered { get; set; }

    [JsonIgnore] public PlannedSegment? Current => Index < Segments.Count ? Segments[Index] : null;
    [JsonIgnore] public Aim? CurrentAim => AimIndex < Aims.Count ? Aims[AimIndex] : null;
    [JsonIgnore] public bool AtEnding => Current?.Type == SegmentCatalogue.Ending;
}
