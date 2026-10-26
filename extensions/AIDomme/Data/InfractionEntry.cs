using System;

namespace AIDomme.Data;

/// <summary>What type of infraction did the user commit?</summary>
public enum InfractionType
{
    /// <summary>User came without permission.</summary>
    UnauthorizedOrgasm = 0,
    /// <summary>User ruined an orgasm (stopped stimulation while cumming).</summary>
    RuinedOrgasm = 1,
    /// <summary>User failed a game challenge.</summary>
    GameFailure = 2,
    /// <summary>User disobeyed an instruction.</summary>
    Disobedience = 3
}

/// <summary>
/// Records a user-reported or system-detected infraction so the punishment engine
/// and cum-chance formula can factor it in.
/// </summary>
public sealed class InfractionEntry : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }

    public DateTime TimestampUtc { get; set; }

    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }

    public InfractionType Type { get; set; }

    /// <summary>Optional free-text context.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Whether a punishment has already been assigned for this infraction.</summary>
    public bool PunishmentAssigned { get; set; }
}
