namespace AIDomme.Sessions;

/// <summary>
/// What a step wants from a method. <paramref name="MinCloseness"/> is set when he must reach the
/// edge on it: a method that can barely get him there (a gentle finger, a teasing pinch) is never
/// chosen for edging while a closer one is available. <paramref name="MaxCloseness"/> asks for the
/// opposite on purpose: edging the hard way, on an intense grip that only just gets him there.
/// </summary>
public sealed record MethodPreference(int IntensityCeiling, bool Tease = false, bool Torture = false, bool Closeness = false, bool Basic = false,
    int MinCloseness = 0, int MaxCloseness = 10)
{
    /// <summary>How close a method must get him (Closeness 0–10) for her to ask for edges on it.</summary>
    public const int EdgeReach = 5;
    /// <summary>Edging the hard way: possible (Closeness at least 1) but a struggle (at most 4).</summary>
    public const int HardReachMin = 1, HardReachMax = 4;
}

/// <summary>Who chooses the stimulation method.</summary>
public interface IMethodAuthority
{
    bool DommeChooses { get; }
    MethodInfo Choose(SessionState state, MethodPreference preference);
}

/// <summary>Who sets stroke speed, timed stimulation and rests.</summary>
public interface IPacingAuthority
{
    bool DommeSets { get; }
    /// <summary>A speed in [min, max] when the domme paces; 0 ("your own pace") otherwise.</summary>
    int Speed(IRandom random, int min, int max);
    /// <summary>The metronome tempo for a speed; silent when the user paces.</summary>
    int Tempo(int speed);
}

/// <summary>Who sets edge targets and holds.</summary>
public interface IEdgeAuthority
{
    bool DommeSets { get; }
}

/// <summary>Who picks what the user looks at.</summary>
public interface IContentAuthority
{
    bool DommeDirects { get; }
}

/// <summary>Whether the domme may interrupt with a game.</summary>
public interface IGameAuthority
{
    bool MayPlay(SessionState state);
}

/// <summary>
/// Who answers "may I cum?". Always the domme in the current modes; the same rules decide her own
/// ending and the user's requests, so she behaves the same whoever sets the pace.
/// </summary>
public interface IPermissionAuthority
{
    double Chance(SessionState state, bool requested);
}

/// <summary>The authorities for one session, resolved from its contract.</summary>
public sealed record Authorities(IMethodAuthority Methods, IPacingAuthority Pacing, IEdgeAuthority Edges,
    IContentAuthority Content, IGameAuthority Games, IPermissionAuthority Permission)
{
    public static Authorities For(SessionContract contract, IRandom random) => new(
        contract.Methods == Authority.Domme ? new DommeMethods(random) : new UserMethods(),
        contract.Pacing == Authority.Domme ? DommePacing.Instance : UserPacing.Instance,
        new EdgeAuthority(contract.Edges == Authority.Domme),
        new ContentAuthority(contract.Content == Authority.Domme),
        new GameAuthority(contract.Games && contract.Pacing == Authority.Domme),
        DommePermission.Instance);

    /// <summary>Anything to plan at all? In free play the domme only reacts and decides the ending.</summary>
    public bool PlansSteps => Pacing.DommeSets || Edges.DommeSets;

    private sealed record EdgeAuthority(bool DommeSets) : IEdgeAuthority;
    private sealed record ContentAuthority(bool DommeDirects) : IContentAuthority;
    private sealed record GameAuthority(bool Enabled) : IGameAuthority
    {
        public bool MayPlay(SessionState state) => Enabled;
    }
}

public sealed class DommePacing : IPacingAuthority
{
    public static readonly DommePacing Instance = new();
    public bool DommeSets => true;
    public int Speed(IRandom random, int min, int max) => random.Next(Math.Clamp(min, 1, 10), Math.Clamp(max, min, 10) + 1);
    public int Tempo(int speed) => Sessions.Tempo.FromSpeed(speed);
}

public sealed class UserPacing : IPacingAuthority
{
    public static readonly UserPacing Instance = new();
    public bool DommeSets => false;
    public int Speed(IRandom random, int min, int max) => 0;
    public int Tempo(int speed) => 0;
}

public sealed class UserMethods : IMethodAuthority
{
    public bool DommeChooses => false;
    public MethodInfo Choose(SessionState state, MethodPreference preference) => MethodInfo.UserChoice(state.Methods);
}

/// <summary>The domme's method choice: varied, but willing to stay on a good grip for a while.</summary>
public sealed class DommeMethods(IRandom random) : IMethodAuthority
{
    public bool DommeChooses => true;

    public MethodInfo Choose(SessionState state, MethodPreference p)
    {
        var all = state.Methods;
        if (all.Count == 0) return MethodInfo.UserChoice(all);
        var pool = all.Where(m => m.Intensity <= p.IntensityCeiling).ToList();
        if (pool.Count == 0) pool = all.ToList();
        if (p.MinCloseness > 0)
        {
            // Edging needs a method that gets him there: within the ceiling if possible, then any,
            // and failing that the closest few he has.
            var reach = pool.Where(m => m.Closeness >= p.MinCloseness).ToList();
            if (reach.Count == 0) reach = all.Where(m => m.Closeness >= p.MinCloseness).ToList();
            if (reach.Count == 0) reach = all.OrderByDescending(m => m.Closeness).Take(3).ToList();
            pool = reach;
        }
        // A torturous or teasing step means it: grips that really are, whenever he has any.
        // Under a low intensity ceiling, a little above it rather than a grip that isn't torturous at all,
        // and failing that the most torturous few he has.
        if (p.Torture)
            pool = pool.Where(m => m.TortLevel >= 5).ToList() is { Count: > 0 } torturous ? torturous
                : all.Where(m => m.TortLevel >= 5 && m.Intensity <= p.IntensityCeiling + 2).ToList() is { Count: > 0 } near ? near
                : pool.OrderByDescending(m => m.TortLevel).Take(3).ToList();
        else if (p.Tease && pool.Where(m => m.Tease >= 4).ToList() is { Count: > 0 } teasing) pool = teasing;
        if (p.MaxCloseness < 10)
        {
            var hard = all.Where(m => m.Closeness >= p.MinCloseness && m.Closeness <= p.MaxCloseness && m.Intensity <= Math.Min(10, p.IntensityCeiling + 2)).ToList();
            if (hard.Count > 0) pool = hard;
        }

        // Once lube is on, dry methods are impractical.
        if (state.LubeActive)
        {
            var lubed = pool.Where(m => m.RequiresLube).ToList();
            // Too few lubed grips under the ceiling would mean the same one all night: reach a little higher.
            if (lubed.Count < 3)
                lubed = all.Where(m => m.RequiresLube && m.Intensity <= p.IntensityCeiling + 2 && (p.MinCloseness == 0 || m.Closeness >= p.MinCloseness)).ToList();
            if (lubed.Count == 0) lubed = all.Where(m => m.RequiresLube).ToList();
            if (lubed.Count > 0) pool = lubed;
            // Widening for lube must not undo a torturous or teasing step's choice.
            if (p.Torture && pool.Where(m => m.TortLevel >= 5).ToList() is { Count: > 0 } stillTorturous) pool = stillTorturous;
            else if (p.Tease && pool.Where(m => m.Tease >= 4).ToList() is { Count: > 0 } stillTeasing) pool = stillTeasing;
        }

        // Deliberate repetition: stay on a basic grip for a step or two before switching.
        if (state.ChainMethodId is int chain && state.ChainStepsRemaining > 0 && !p.Torture
            && pool.FirstOrDefault(m => m.Id == chain) is { Closeness: >= 3, Tease: < 6 } chained && chained.Closeness >= p.MinCloseness && random.Chance(55))
        {
            state.ChainStepsRemaining--;
            return Track(state, chained);
        }

        int last = state.RecentMethodIds.Count > 0 ? state.RecentMethodIds[^1] : -1;
        var weighted = pool.Select(m =>
        {
            int index = state.RecentMethodIds.LastIndexOf(m.Id);
            double w = index >= 0 ? 1.0 / (1 + (state.RecentMethodIds.Count - index)) : 2.0;
            // Accidental back-to-back repeats feel mechanical; chains above are the deliberate kind.
            if (m.Id == last && pool.Count > 1) w *= 0.02;
            if (p.Tease) w *= 1.0 + m.Tease / 10.0;
            if (p.Torture) w *= 1.0 + m.TortLevel / 10.0;
            if (p.Closeness) w *= 1.0 + m.Closeness / 5.0;
            if (p.Basic) w *= 1.0 + Math.Max(0.1, m.Closeness / 10.0 * (1.0 - m.Tease / 15.0) * (1.0 - m.TortLevel / 15.0)) * 4.0;
            if (!state.LubeActive && m.RequiresLube) w *= 0.4;
            return (m, w);
        }).ToList();
        return Track(state, random.Weighted(weighted, pool[0]));
    }

    private static MethodInfo Track(SessionState state, MethodInfo method)
    {
        state.RecentMethodIds.Add(method.Id);
        if (state.RecentMethodIds.Count > 10) state.RecentMethodIds.RemoveAt(0);
        return method;
    }
}

/// <summary>
/// The domme's permission rules. The same inputs give the same odds in every mode: her denial
/// preference, how far through the session the points are, edges so far, how recently the user last
/// came, her mood, and whether she is annoyed or the user keeps asking.
/// </summary>
public sealed class DommePermission : IPermissionAuthority
{
    public static readonly DommePermission Instance = new();

    public double Chance(SessionState s, bool requested) => Chance(s, requested, s.Progress, s.EdgesThisSession);

    /// <summary>The same rules at a projected point in the session (her lean when the plan is made).</summary>
    public double Chance(SessionState s, bool requested, double atProgress, int edges)
    {
        double chance = Math.Clamp(90 - s.Denial * 8.5, 5, 85) + Math.Min(20, edges * 0.8);
        double progress = Math.Clamp(atProgress, 0, 1);
        chance *= progress >= 1 ? 1 : 0.1 + 0.9 * progress * progress;
        double? hours = s.HoursSinceLastOrgasmAtStart is double h ? h + (s.LastActivityUtc - s.StartedAtUtc).TotalHours : null;
        var prefs = s.Endings;
        if (prefs.OrgasmEveryDays > 0 && hours is double since)
        {
            // His cadence: well short of it she mostly says no; past it she comes round.
            double ratio = since / 24 / prefs.OrgasmEveryDays;
            chance *= ratio switch { < 0.5 => 0.25, < 0.8 => 0.55, < 1 => 0.8, < 1.5 => 1.15, _ => 1.35 };
        }
        else chance *= hours switch { null => 1, < 12 => 0.7, < 24 => 0.85, > 72 => 1.15, _ => 1 };
        chance *= prefs.Denial switch
        {
            DenialPreference.Rarely => 1.5, DenialPreference.Often => 0.6, DenialPreference.Mostly => 0.3, _ => 1,
        };
        chance *= s.Mood.Mood switch
        {
            DommeMood.Cruel => 0.7, DommeMood.Teasing => 0.85, DommeMood.Relentless => 0.9, DommeMood.Playful => 1.1, _ => 1,
        };
        if (s.Retaliation.Active) chance *= 0.3;
        if (s.Contract.Punishment) chance *= 0.6;
        if (requested) chance -= 6 * Math.Max(0, s.RequestsThisSession - 1);
        // He never wants to be denied: at the end she lets him cum (a promise or window still wins).
        if (prefs.Denial == DenialPreference.Never && !requested && progress >= 1) return 100;
        return Math.Clamp(chance + s.PermissionModifier, 1, 95);
    }
}
