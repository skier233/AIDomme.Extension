using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIDome.Sessions.Plan;

/// <summary>What one step of a segment asks for.</summary>
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

/// <summary>How a segment picks its methods.</summary>
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

/// <summary>The shape of the steps a segment is made of. Ranges are [min, max].</summary>
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
}

/// <summary>What happens when he begs for a rest during a segment of this type.</summary>
public sealed record RestBegRule
{
    /// <summary>Chance she grants it.</summary>
    public double Grant { get; init; }
    /// <summary>What she switches to instead of a real rest (segment tags). Empty: a plain rest.</summary>
    public string[] SwapTo { get; init; } = [];
    /// <summary>Whether she comes back to finish this segment afterwards.</summary>
    public bool Resume { get; init; }
}

/// <summary>
/// A segment type: one goal she can pursue for a stretch of the session. Everything the plan
/// engine knows about ordering comes from these properties; there are no rules for particular
/// segments, so adding a type is adding an entry to the catalogue.
/// </summary>
public sealed record SegmentType
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>What she is trying to make him do or feel.</summary>
    public string Goal { get; init; } = "";
    public string[] Tags { get; init; } = [];
    /// <summary>How hard it is, 0–10, for the session's escalation curve.</summary>
    public int Intensity { get; init; }
    /// <summary>What she must decide for this to work: <c>pacing</c>, <c>edges</c>, <c>methods</c>, <c>games</c>.</summary>
    public string[] Needs { get; init; } = [];
    /// <summary>Share of the session's points budget, [min, max].</summary>
    public double[] Share { get; init; } = [0.06, 0.15];
    /// <summary>Steps, [min, max].</summary>
    public int[] Steps { get; init; } = [1, 8];
    /// <summary>Where in the session it fits: weights for <c>start</c>, <c>middle</c> and <c>late</c>.</summary>
    public Dictionary<string, double> Position { get; init; } = [];
    /// <summary>How much it likes following a segment with each tag (a multiplier; 1 when absent).</summary>
    public Dictionary<string, double> Follows { get; init; } = [];
    /// <summary>Tags the next segment must have: this one does not make sense on its own.</summary>
    public string[] FollowUp { get; init; } = [];
    public bool Repeatable { get; init; }
    /// <summary>Chance of a short rest between its steps.</summary>
    public double RestBetween { get; init; }
    public RestBegRule? OnRestBeg { get; init; }
    /// <summary>Minimum persona dials for her to use it (for example <c>manipulation</c>: 4).</summary>
    public Dictionary<string, int> Persona { get; init; } = [];
    /// <summary>False for types only revisions insert (a punishment).</summary>
    public bool Planned { get; init; } = true;
    public StepTemplate Step { get; init; } = new();

    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);
    public bool HasAny(IEnumerable<string> tags) => tags.Any(Has);
}

public sealed record SegmentCatalogueFile(SegmentType[] Segments, Dictionary<string, Dictionary<string, double>> MoodLenses,
    Dictionary<string, string[]>? Requires = null);

/// <summary>The segment types shipped with the extension, and how each mood leans on their tags.</summary>
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
        var catalogue = new SegmentCatalogue(file.Segments, file.MoodLenses ?? [], file.Requires);
        var problems = catalogue.Validate();
        if (problems.Count > 0) throw new InvalidDataException("The segment catalogue is invalid:\n" + string.Join("\n", problems));
        return catalogue;
    });
    public static SegmentCatalogue Default => Shipped.Value;

    public IReadOnlyList<SegmentType> Types { get; }
    public IReadOnlyDictionary<string, Dictionary<string, double>> MoodLenses { get; }
    /// <summary>Tags a plan must include when the session asks for it (a punishment, a trait he owes).</summary>
    public IReadOnlyDictionary<string, string[]> Requires { get; }

    /// <summary>The session conditions a requirement may name.</summary>
    public static readonly string[] RequirementKeys = ["punishment", "torturous", "teasing", "intense", "mean"];

    public SegmentCatalogue(IEnumerable<SegmentType> types, IReadOnlyDictionary<string, Dictionary<string, double>> moodLenses,
        IReadOnlyDictionary<string, string[]>? requires = null)
    {
        Types = types.ToList();
        MoodLenses = moodLenses;
        Requires = requires ?? new Dictionary<string, string[]>();
    }

    public SegmentType? Find(string id) => Types.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The ending is always last; it is not chosen like the others.</summary>
    public const string Ending = "ending";

    public List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var dup in Types.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)) problems.Add($"Duplicate segment '{dup.Key}'.");
        if (Find(Ending) is null) problems.Add("The catalogue needs an 'ending' segment.");
        var known = new HashSet<string>(Types.SelectMany(x => x.Tags), StringComparer.OrdinalIgnoreCase);
        foreach (var t in Types)
        {
            string at = $"'{t.Id}'";
            if (t.Share is not [var lo, var hi] || lo <= 0 || hi < lo || hi > 0.6) problems.Add($"{at}: share must be [min, max] within (0, 0.6].");
            if (t.Steps is not [var smin, var smax] || smin < 1 || smax < smin) problems.Add($"{at}: steps must be [min, max] from 1.");
            if (t.Intensity is < 0 or > 10) problems.Add($"{at}: intensity runs 0–10.");
            foreach (var need in t.Needs)
                if (need is not ("pacing" or "edges" or "methods" or "games")) problems.Add($"{at}: unknown need '{need}'.");
            foreach (var key in t.Position.Keys)
                if (key is not ("start" or "middle" or "late")) problems.Add($"{at}: unknown position '{key}'.");
            foreach (var tag in t.Follows.Keys.Concat(t.FollowUp).Concat(t.OnRestBeg?.SwapTo ?? []))
                if (!known.Contains(tag)) problems.Add($"{at}: refers to tag '{tag}' that no segment has.");
            foreach (var key in t.Persona.Keys)
                if (key is not ("harsh" or "humiliation" or "manipulation" or "talk")) problems.Add($"{at}: unknown persona dial '{key}'.");
            var s = t.Step;
            foreach (var range in new[] { s.Speed, s.Seconds, s.Count, s.Hold })
                if (range is not [var min, var max] || min < 0 || max < min) problems.Add($"{at}: a step range must be [min, max].");
            if (s.Method.Policy is not ("same" or "rotate" or "swap" or "favorite" or "hated")) problems.Add($"{at}: unknown method policy '{s.Method.Policy}'.");
            if (s.Method.Prefer is not ("tease" or "torture" or "closeness" or "basic" or "any")) problems.Add($"{at}: unknown method preference '{s.Method.Prefer}'.");
        }
        foreach (var (key, tags) in Requires)
        {
            if (!RequirementKeys.Contains(key)) problems.Add($"Unknown requirement '{key}'.");
            foreach (var tag in tags)
                if (!known.Contains(tag)) problems.Add($"Requirement '{key}' refers to tag '{tag}' that no segment has.");
        }
        foreach (var (mood, lens) in MoodLenses)
        {
            if (!Enum.TryParse<DommeMood>(mood, true, out _)) problems.Add($"Mood lens for unknown mood '{mood}'.");
            foreach (var tag in lens.Keys)
                if (!known.Contains(tag)) problems.Add($"Mood lens '{mood}' refers to tag '{tag}' that no segment has.");
        }
        return problems;
    }
}

/// <summary>One segment in a session's plan, with how far it has got.</summary>
public sealed class PlannedSegment
{
    public string Type { get; set; } = "";
    /// <summary>Share of the points budget this segment should take.</summary>
    public double Share { get; set; }
    public int MinSteps { get; set; }
    public int MaxSteps { get; set; }
    public int Steps { get; set; }
    public double PointsAtStart { get; set; }
    public bool Started { get; set; }
    /// <summary>The method fixed for the segment (policy <c>same</c> or <c>favorite</c>).</summary>
    public int? MethodId { get; set; }
    /// <summary>Why it is in the plan: <c>plan</c>, or the revision that put it there.</summary>
    public string Origin { get; set; } = "plan";
}

/// <summary>
/// Her plan for the session: a sequence of segments ending with the ending. It is made when the
/// session starts and revised by events; every revision is logged for the debug view and
/// transcripts.
/// </summary>
public sealed class SessionPlan
{
    public List<PlannedSegment> Segments { get; set; } = [];
    public int Index { get; set; }
    public List<string> Revisions { get; set; } = [];
    public int Foreshadowed { get; set; }
    /// <summary>The user's preferences for segment types, as they were when the session began.</summary>
    public Dictionary<string, SegmentPreference> Preferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public PlannedSegment? Current => Index < Segments.Count ? Segments[Index] : null;
    [JsonIgnore] public bool AtEnding => Current?.Type == SegmentCatalogue.Ending;
}
