using AIDome.Data;

namespace AIDome.Sessions;

/// <summary>Who decides a concern during a session.</summary>
public enum Authority { Domme, User }

/// <summary>The four supported ways to play, plus any other valid combination of authorities.</summary>
public enum SessionMode
{
    /// <summary>The domme picks the content, methods, pacing, edges, games and ending.</summary>
    FullGuided,
    /// <summary>The user browses their own content; the domme runs everything else.</summary>
    OwnContent,
    /// <summary>The user picks content and methods; the domme sets pacing, edges, holds and the ending.</summary>
    OwnMethods,
    /// <summary>The user does everything and tracks edges; the domme owns permission and the ending.</summary>
    FreePlay,
    Custom,
}

/// <summary>Preferences on a 1–10 scale; -1 lets the domme choose from recent sessions.</summary>
public sealed record SessionPreferences
{
    public int Intensity { get; init; } = -1;
    public int Length { get; init; } = -1;
    public int Denial { get; init; } = -1;
    public int Mean { get; init; } = -1;
}

/// <summary>What the domme may ask for. Methods outside these limits are never prescribed.</summary>
public sealed record SessionLimits
{
    public ToyCategory[] Toys { get; init; } = [];
    public bool Anal { get; init; }
    public bool Bondage { get; init; }
    public bool Cei { get; init; }
    public bool SelfOral { get; init; }
    /// <summary>Restricts the method pool; empty means every method the limits allow.</summary>
    public int[] AllowedMethodIds { get; init; } = [];
}

/// <summary>
/// Everything settled before play begins. The engine consults the authority for each concern,
/// never the mode name, so new combinations need no engine changes. Permission and the ending are
/// always the domme's in the current modes.
/// </summary>
public sealed record SessionContract
{
    public int Version { get; init; } = 1;
    public SessionMode Mode { get; init; } = SessionMode.FullGuided;
    public Authority Content { get; init; } = Authority.Domme;
    public Authority Methods { get; init; } = Authority.Domme;
    public Authority Pacing { get; init; } = Authority.Domme;
    public Authority Edges { get; init; } = Authority.Domme;
    /// <summary>Whether the domme may run games. Games need her to own pacing.</summary>
    public bool Games { get; init; } = true;
    public SessionPreferences Preferences { get; init; } = new();
    public SessionLimits Limits { get; init; } = new();
    /// <summary>A harsher session, usually owed as a punishment.</summary>
    public bool Punishment { get; init; }
    /// <summary>Debug sessions can be ended by the user and never count against them.</summary>
    public bool Debug { get; init; }
    /// <summary>Debug aid: every step is a game.</summary>
    public bool GamesOnly { get; init; }
    /// <summary>Decided before starting and enforced by the server for the whole session.</summary>
    public bool CanChangeDuringSession { get; init; }

    public static SessionContract Preset(SessionMode mode) => mode switch
    {
        SessionMode.FullGuided => new() { Mode = mode },
        SessionMode.OwnContent => new() { Mode = mode, Content = Authority.User },
        SessionMode.OwnMethods => new() { Mode = mode, Content = Authority.User, Methods = Authority.User },
        SessionMode.FreePlay => new()
        {
            Mode = mode, Content = Authority.User, Methods = Authority.User, Pacing = Authority.User,
            Edges = Authority.User, Games = false,
        },
        _ => throw new ArgumentException("Custom is not a preset."),
    };

    /// <summary>The preset these authorities match, or Custom.</summary>
    public SessionMode DerivedMode()
    {
        foreach (var mode in new[] { SessionMode.FullGuided, SessionMode.OwnContent, SessionMode.OwnMethods, SessionMode.FreePlay })
        {
            var preset = Preset(mode);
            if (preset.Content == Content && preset.Methods == Methods && preset.Pacing == Pacing && preset.Edges == Edges)
                return mode;
        }
        return SessionMode.Custom;
    }

    /// <summary>Checks the contract and returns it normalized (mode derived from the authorities).</summary>
    public SessionContract Validate()
    {
        if (Version != 1) throw new ArgumentException("Unsupported session contract version.");
        foreach (var value in new[] { Content, Methods, Pacing, Edges })
            if (!Enum.IsDefined(value)) throw new ArgumentException("Unknown authority.");
        var p = Preferences ?? throw new ArgumentException("Preferences are required.");
        foreach (var value in new[] { p.Intensity, p.Length, p.Denial, p.Mean })
            if (value != -1 && value is < 1 or > 10) throw new ArgumentException("Preferences use 1–10, or -1 for automatic.");
        var limits = Limits ?? throw new ArgumentException("Limits are required.");
        if (limits.Toys is null || limits.Toys.Any(x => x == ToyCategory.None || !Enum.IsDefined(x)))
            throw new ArgumentException("Unknown toy.");
        if (limits.AllowedMethodIds is null || limits.AllowedMethodIds.Length > 200 || limits.AllowedMethodIds.Any(x => x <= 0))
            throw new ArgumentException("Invalid allowed methods.");
        if (Games && Pacing != Authority.Domme)
            throw new ArgumentException("Games need the domme to set the pace.");
        if (GamesOnly && !Games) throw new ArgumentException("Games-only sessions need games.");
        if (GamesOnly && !Debug) throw new ArgumentException("Games-only is a debug option.");
        return this with
        {
            Mode = DerivedMode(),
            Limits = limits with { Toys = limits.Toys.Distinct().ToArray(), AllowedMethodIds = limits.AllowedMethodIds.Distinct().ToArray() },
        };
    }

    /// <summary>Whether a method may be prescribed under these limits.</summary>
    public bool Allows(MethodInfo method)
        => (method.ToyCategory == ToyCategory.None || Limits.Toys.Contains(method.ToyCategory))
           && (Limits.AllowedMethodIds.Length == 0 || Limits.AllowedMethodIds.Contains(method.Id));
}
