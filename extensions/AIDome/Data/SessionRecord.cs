using System;

namespace AIDome.Data;

/// <summary>How the session ended from the user's perspective.</summary>
public enum SessionOutcome
{
    /// <summary>Session is still in progress or was abandoned without reporting.</summary>
    Unknown = 0,
    /// <summary>User was denied permission to cum.</summary>
    Denied = 1,
    /// <summary>User came with permission (full orgasm).</summary>
    AllowedOrgasm = 2,
    /// <summary>User came without permission (unauthorized).</summary>
    UnauthorizedOrgasm = 3,
    /// <summary>User ruined the orgasm (came without permission but stopped stimulation).</summary>
    RuinedOrgasm = 4
}

public sealed class SessionRecord : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }

    public DateTime StartedAtUtc { get; set; }

    public DateTime LastInteractionUtc { get; set; }

    // ── Tunable session parameters (1-10, or -1 = auto) ──
    public int Intensity { get; set; }
    public int Length { get; set; }
    public int Denial { get; set; }
    public int Kink { get; set; }
    public int Mean { get; set; }

    // ── Toy / kink opt-in flags ──
    public bool AllowToys { get; set; }
    public bool AllowVibrator { get; set; }
    public bool AllowFleshlight { get; set; }
    public bool AllowAutoblow { get; set; }
    public bool AllowProstate { get; set; }
    public bool AllowAnal { get; set; }
    public bool AllowBondage { get; set; }
    public bool AllowCEI { get; set; }
    public bool AllowSelfOral { get; set; }

    // ── Session flags ──
    public bool IsDebugSession { get; set; }

    /// <summary>
    /// Anti-cheat: set to true when a non-debug session starts.
    /// Cleared only when the session ends normally. If the user closes
    /// the app mid-session, this stays true and triggers a confession
    /// prompt on the next session.
    /// </summary>
    public bool SuspectedUnauthorizedOrgasm { get; set; }

    // ── Outcome ──
    public SessionOutcome Outcome { get; set; }

    /// <summary>Accumulated points this session (for point economy).</summary>
    public int Points { get; set; }

    /// <summary>The session mode (FullGuided, OwnContent, OwnMethods, FreePlay, Custom, or Homework).</summary>
    public string? Mode { get; set; }

    /// <summary>The accepted session contract, as JSON.</summary>
    public string? ContractJson { get; set; }

    // ── Navigation ──
    public ICollection<EdgeEntry> Edges { get; set; } = new List<EdgeEntry>();
    public ICollection<OrgasmEntry> Orgasms { get; set; } = new List<OrgasmEntry>();
    public ICollection<InstructionStep> Instructions { get; set; } = new List<InstructionStep>();
    public ICollection<PunishmentEntry> Punishments { get; set; } = new List<PunishmentEntry>();
    public ICollection<GameChallenge> Games { get; set; } = new List<GameChallenge>();
}
