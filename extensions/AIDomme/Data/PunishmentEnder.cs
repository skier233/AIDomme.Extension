using System.ComponentModel.DataAnnotations;

namespace AIDomme.Data;

/// <summary>
/// A specific way to end a punishment session. These are more intense and humiliating
/// than normal session finishes and are ranked by severity, anticipation, and other factors.
/// Unlike StimMethods (which describe ongoing stimulation), PunishmentEnders describe
/// a complete finish scenario — often multi-step.
/// </summary>
public sealed class PunishmentEnder : IDefinition<PunishmentEnder>, IRemovable
{
    /// <summary>Null for a shared default; the owning account for a personal definition or override.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? UserId { get; set; }
    /// <summary>For a personal override, the shared definition it replaces.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int? BaseId { get; set; }
    public int Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>1-10. How severe/painful the punishment ending is.</summary>
    public int Severity { get; set; }

    /// <summary>1-10. How much dread/anticipation this creates when announced.</summary>
    public int Anticipation { get; set; }

    /// <summary>1-10. How humiliating this is.</summary>
    public int Humiliation { get; set; }

    /// <summary>Whether this requires bondage (duct tape, restraints).</summary>
    public bool RequiresBondage { get; set; }

    /// <summary>Whether this involves CEI.</summary>
    public bool RequiresCEI { get; set; }

    /// <summary>Whether this involves self-oral.</summary>
    public bool RequiresSelfOral { get; set; }

    /// <summary>Multi-line instruction text — the full punishment ending script.</summary>
    [MaxLength(2048)]
    public string InstructionText { get; set; } = string.Empty;

    /// <summary>Short domme announcement when this ender is selected (dread-building).</summary>
    [MaxLength(512)]
    public string AnnouncementText { get; set; } = string.Empty;

    /// <summary>False for endings that deny (they can serve during a denial window).</summary>
    public bool EndsInOrgasm { get; set; } = true;

    /// <summary>Taken out of this account's library: never used, but kept for history.</summary>
    public bool Removed { get; set; }

    /// <summary>A detached copy presented under the definition's stable ID.</summary>
    public PunishmentEnder AsDefinition(int id)
    {
        var copy = (PunishmentEnder)MemberwiseClone();
        copy.Id = id;
        copy.UserId = null;
        copy.BaseId = null;
        return copy;
    }
}
