namespace AIDome.Sessions.Plan;

public enum EndingKind { Full, Ruined, Denied, PunishmentEnding }

/// <summary>What she lets on about the ending before it comes.</summary>
public enum Presentation
{
    /// <summary>She hints at the truth.</summary>
    Honest,
    /// <summary>She hints at the opposite.</summary>
    Lie,
    /// <summary>She says nothing about it.</summary>
    Silent,
}

/// <summary>
/// The ending she leans toward, and the chance draws that decide it. The draws are made once,
/// when the session begins; her decision at the end compares the same draw with the odds at that
/// moment, so the ending she hinted at is the one she gives unless what he did changed the odds.
/// </summary>
public sealed class EndingLean
{
    public EndingKind Kind { get; set; } = EndingKind.Denied;
    public Presentation Presentation { get; set; } = Presentation.Silent;
    public double Draw { get; set; }
    public double RuinDraw { get; set; }
    public string Reason { get; set; } = "";
    /// <summary>What her hints told him: that he would cum (true), that he wouldn't (false), or nothing yet.</summary>
    public bool? HintedOrgasm { get; set; }

    /// <summary>
    /// How her hints compare with how it ends: "told:Honest" when they came true, "told:Lie" when
    /// they didn't, "told:Silent" when she never hinted.
    /// </summary>
    public string Told(bool orgasm) => HintedOrgasm is bool hinted ? hinted == orgasm ? "told:Honest" : "told:Lie" : "told:Silent";
}

/// <summary>
/// Her ending authority: the lean when a session begins, and the same rules when she decides. Free
/// play and guided sessions share it, so she ends sessions the same way whoever set the pace.
/// </summary>
public static class EndingAuthority
{
    /// <summary>Edges she expects by the end of a session, for projecting her odds.</summary>
    public static int ExpectedEdges(SessionState s) => (int)Math.Round(s.Budget / 9);

    public static EndingLean Lean(SessionState s, IRandom r)
    {
        var lean = new EndingLean { Draw = r.NextDouble(), RuinDraw = r.NextDouble() };
        var persona = s.Persona;
        double lie = persona.Manipulation * 4.5 + s.Mood.Mood switch { DommeMood.Cruel or DommeMood.Teasing => 10, _ => 0 };
        double roll = r.NextDouble() * 100;
        lean.Presentation = roll < lie ? Presentation.Lie : roll < lie + 35 ? Presentation.Silent : Presentation.Honest;
        Refresh(s, lean, projected: true);
        return lean;
    }

    /// <summary>
    /// Re-reads the lean from the current facts: the projected odds at the end of the session while
    /// it runs, or the actual odds when she decides.
    /// </summary>
    public static void Refresh(SessionState s, EndingLean lean, bool projected)
    {
        // A promise or a denial window that still covers the end of the session settles it.
        DateTime end = projected ? s.StartedAtUtc.AddHours(1) : s.LastActivityUtc;
        if (s.DenialUntilUtc is DateTime until && until > end)
        {
            lean.Kind = s.OwedEndings > 0 ? EndingKind.PunishmentEnding : EndingKind.Denied;
            lean.Reason = "a promise or a denial window";
            return;
        }
        if (s.OwedEndings > 0)
        {
            lean.Kind = EndingKind.PunishmentEnding;
            lean.Reason = "an owed punishment ending";
            return;
        }
        double chance = projected
            ? DommePermission.Instance.Chance(s, false, 1, Math.Max(s.EdgesThisSession, ExpectedEdges(s)))
            : DommePermission.Instance.Chance(s, false);
        bool allowed = lean.Draw * 100 < chance;
        if (!allowed)
        {
            lean.Kind = EndingKind.Denied;
            lean.Reason = $"{chance:0}% chance she lets him cum; the draw made at the start says no";
            return;
        }
        bool owedRuin = s.OwedRuins > 0 && s.RuinsAllowed;
        lean.Kind = owedRuin || lean.RuinDraw < RuinChance(s) ? EndingKind.Ruined : EndingKind.Full;
        lean.Reason = owedRuin ? "an owed ruin"
            : $"{chance:0}% chance she lets him cum; the draw made at the start says yes"
              + (lean.Kind == EndingKind.Ruined ? $", and her {RuinChance(s):P0} ruin chance came up" : "");
    }

    /// <summary>How likely an allowed orgasm is ruined by her own choice, within his preference.</summary>
    public static double RuinChance(SessionState s)
    {
        if (!s.RuinsAllowed) return 0;
        double chance = 0.03 + s.Mean * 0.022 + (s.Contract.Punishment ? 0.15 : 0);
        return s.Endings.Ruins switch
        {
            RuinPreference.PunishmentOnly => s.Contract.Punishment ? chance : 0,
            RuinPreference.Often => Math.Min(0.8, chance * 2),
            _ => chance,
        };
    }
}
