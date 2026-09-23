using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AIDome.Speech;

/// <summary>
/// How long text is kept from repeating. Short stock lines only avoid repeating within a session;
/// distinctive lines and stories are tracked across sessions so they stay rare.
/// </summary>
public enum Cooldown
{
    /// <summary>Not repeated within a session.</summary>
    Session,
    /// <summary>Not within the last 5 sessions or 14 days.</summary>
    Occasional,
    /// <summary>Not within the last 20 sessions or 60 days.</summary>
    Rare,
    /// <summary>Told once, ever.</summary>
    Once,
}

/// <summary>
/// One module in a line file. A stock module holds alternative lines for an act (<see cref="Variants"/>);
/// a story module holds a sequence told across quiet moments (<see cref="Lines"/>). Conditions
/// (<see cref="When"/>) are tested against what she knows; see <see cref="Conditions"/>.
/// </summary>
public sealed record LineModule
{
    public string Id { get; init; } = "";
    public SpeechAct Act { get; init; }
    public SpeechStream? Stream { get; init; }
    public Cooldown Cooldown { get; init; } = Cooldown.Session;
    public string[] When { get; init; } = [];
    public double Weight { get; init; } = 1;
    public string[] Variants { get; init; } = [];
    /// <summary>A story: lines told in order, one per quiet moment.</summary>
    public string[] Lines { get; init; } = [];
    /// <summary>Seconds between the lines of a story.</summary>
    public int Spacing { get; init; } = 12;
    /// <summary>A command straight after this line needs no bridge.</summary>
    public bool NoBridge { get; init; }

    [JsonIgnore] public bool IsStory => Lines.Length > 0;
    /// <summary>Seconds of quiet a story needs to be told without being interrupted.</summary>
    [JsonIgnore] public int Needs => IsStory ? (Lines.Length - 1) * Spacing + 8 : 0;
}

public sealed record LineFile(LineModule[] Modules);

/// <summary>The line files shipped with the extension, loaded once and validated.</summary>
public sealed class LineLibrary
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Lazy<LineLibrary> Shipped = new(() => Load(typeof(LineLibrary).Assembly));
    public static LineLibrary Default => Shipped.Value;

    public IReadOnlyList<LineModule> Modules { get; }
    private readonly ILookup<SpeechAct, LineModule> byAct;

    public LineLibrary(IEnumerable<LineModule> modules)
    {
        Modules = modules.ToList();
        byAct = Modules.ToLookup(x => x.Act);
    }

    public IEnumerable<LineModule> For(SpeechAct act) => byAct[act];

    public static LineLibrary Load(System.Reflection.Assembly assembly)
    {
        var modules = new List<LineModule>();
        foreach (var name in assembly.GetManifestResourceNames().Where(x => x.StartsWith("AIDome.Lines.", StringComparison.Ordinal)).OrderBy(x => x))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            var file = JsonSerializer.Deserialize<LineFile>(stream, Json) ?? throw new InvalidDataException($"{name} is empty.");
            modules.AddRange(file.Modules);
        }
        var library = new LineLibrary(modules);
        var problems = library.Validate();
        if (problems.Count > 0) throw new InvalidDataException("Line files are invalid:\n" + string.Join("\n", problems));
        return library;
    }

    /// <summary>Slot names lines may use. A typo in a slot name is a validation error.</summary>
    public static readonly HashSet<string> KnownSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        "instruction", "name", "grip", "method", "pace", "time", "seconds", "count", "times", "hold",
        "offer", "days", "edges", "edges.today", "edges.sinceorgasm", "begs", "made", "target", "bet", "rules", "reveal",
        "fate", "win", "loss", "stakes", "promise", "owed", "reason", "left", "ender", "performer", "tag", "title",
        "favorite", "last.days", "orgasm.days", "denial.streak", "streak", "segment", "answer", "owed.edges", "sessions", "last.begs",
    };

    private static readonly Regex Slot = new(@"\[([A-Za-z.]+)\]", RegexOptions.Compiled);

    public static IEnumerable<string> SlotsIn(string text) => Slot.Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>Every problem found, empty when the library is sound.</summary>
    public List<string> Validate()
    {
        var problems = new List<string>();
        foreach (var dup in Modules.GroupBy(x => x.Id).Where(g => g.Count() > 1))
            problems.Add($"Duplicate module id '{dup.Key}'.");
        foreach (var m in Modules)
        {
            string where = $"'{m.Id}'";
            if (string.IsNullOrWhiteSpace(m.Id)) problems.Add("A module has no id.");
            if (!Enum.IsDefined(m.Act)) problems.Add($"{where}: unknown act.");
            if (m.Variants.Length == 0 && m.Lines.Length == 0) problems.Add($"{where}: no variants and no lines.");
            if (m.Variants.Length > 0 && m.Lines.Length > 0) problems.Add($"{where}: either variants or story lines, not both.");
            if (m.Weight <= 0) problems.Add($"{where}: weight must be positive.");
            if (m.IsStory && m.Cooldown == Cooldown.Session) problems.Add($"{where}: a story needs a cooldown across sessions.");
            foreach (var c in m.When)
            {
                try { Conditions.Validate(c); }
                catch (FormatException ex) { problems.Add($"{where}: {ex.Message}"); }
            }
            foreach (var text in m.Variants.Concat(m.Lines))
            {
                if (string.IsNullOrWhiteSpace(text)) problems.Add($"{where}: an empty line.");
                // Her style: plain sentences, no typographic tells.
                if (text.Contains('—') || text.Contains('–')) problems.Add($"{where}: dash in \"{text}\".");
                if (text.Contains("  ")) problems.Add($"{where}: double space in \"{text}\".");
                foreach (var slot in SlotsIn(text))
                    if (!KnownSlots.Contains(slot)) problems.Add($"{where}: unknown slot [{slot}] in \"{text}\".");
            }
        }
        return problems;
    }
}
