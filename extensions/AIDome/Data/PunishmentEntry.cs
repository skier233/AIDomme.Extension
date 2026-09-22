using System;

namespace AIDome.Data;

/// <summary>Category of punishment.</summary>
public enum PunishmentCategory
{
    /// <summary>Denial period (N days without cumming).</summary>
    Denial = 0,
    /// <summary>Must cum using an uncomfortable method N times.</summary>
    CumMethod = 1,
    /// <summary>Forced edging / overload.</summary>
    Overload = 2,
    /// <summary>Tease overload.</summary>
    TeaseOverload = 3,
    /// <summary>Other / custom.</summary>
    Other = 4
}

/// <summary>
/// A punishment assigned during or after a session.
/// </summary>
public sealed class PunishmentEntry : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }

    public PunishmentCategory Category { get; set; }

    /// <summary>Severity tier (1-8, higher = worse).</summary>
    public int Tier { get; set; }

    /// <summary>Human-readable description of the punishment.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>How many remaining instances the user must endure (e.g. "next 3 times you cum").</summary>
    public int RemainingCount { get; set; }

    /// <summary>For denial punishments: the UTC date the denial period ends.</summary>
    public DateTime? DenialEndsAtUtc { get; set; }

    /// <summary>Whether the punishment has been fully served.</summary>
    public bool Completed { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
