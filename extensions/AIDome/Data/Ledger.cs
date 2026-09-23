using System.ComponentModel.DataAnnotations;
using AIDome.Sessions;

namespace AIDome.Data;

/// <summary>
/// One entry in the punishment ledger: something owed, where it came from, and whether it has been
/// served. Entries are never deleted; serving one records where and when.
/// </summary>
public sealed class LedgerEntry : IUserOwned
{
    public int Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public LedgerKind Kind { get; set; }
    /// <summary>Days, sessions, endings, edges or ruins, as first owed.</summary>
    public int Amount { get; set; }
    /// <summary>What is still owed (denial: days as first owed; the window decides when it is served).</summary>
    public int Remaining { get; set; }
    public int Severity { get; set; }
    public LedgerOrigin Origin { get; set; }
    public string Reason { get; set; } = "";
    public int? SessionId { get; set; }
    public Guid? CommitmentId { get; set; }
    public Guid? OfferId { get; set; }
    public Guid? FollowUpId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>Denial window.</summary>
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public LedgerStatus Status { get; set; }
    /// <summary>The session serving it now, or the one that served it.</summary>
    public int? ServingSessionId { get; set; }
    public DateTime? ServedAtUtc { get; set; }
}

public enum CommitmentStatus
{
    /// <summary>Her response is waiting for the user to accept or decline.</summary>
    Pending,
    Active,
    Kept,
    Broken,
    Declined,
}

/// <summary>A promise the user made, as she accepted (or countered) it, with the stakes if broken.</summary>
public sealed class CommitmentRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public CommitmentStatus Status { get; set; }
    public CommitmentScope Scope { get; set; }
    public int Days { get; set; }
    public bool NoOrgasm { get; set; }
    public bool Long { get; set; }
    public bool Intense { get; set; }
    public bool Torturous { get; set; }
    public bool Mean { get; set; }
    public bool Teasing { get; set; }
    public string? Note { get; set; }
    /// <summary>The stakes, as JSON: what breaking it adds to the ledger.</summary>
    public string Stakes { get; set; } = "[]";
    /// <summary>Her words when she answered.</summary>
    public string Response { get; set; } = "";
    public bool Countered { get; set; }
    /// <summary>What the user first proposed, as JSON, when she countered.</summary>
    public string? Proposed { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    /// <summary>For a session promise: the session it is bound to once one starts.</summary>
    public int? SessionId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    /// <summary>What was observed and what the user said, when it was resolved.</summary>
    public string? Resolution { get; set; }

    public CommitmentTerms Terms => new()
    {
        Scope = Scope, Days = Days, NoOrgasm = NoOrgasm, Long = Long, Intense = Intense, Torturous = Torturous, Mean = Mean,
        Teasing = Teasing, Note = Note,
    };
}

public enum OfferStatus { Proposed, Accepted, Declined, Expired }

/// <summary>A deal she put to the user during a session, with the parameters as she filled them in.</summary>
public sealed class OfferRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public int? DefinitionId { get; set; }
    public int? SessionId { get; set; }
    public OfferBuys Buys { get; set; }
    public OfferKind Kind { get; set; }
    public int Amount { get; set; }
    public int Hold { get; set; }
    public int Severity { get; set; }
    public double Points { get; set; }
    public OfferFlavor Flavor { get; set; }
    public string Text { get; set; } = "";
    public OfferStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

public enum FollowUpKind
{
    /// <summary>An orgasm without permission: was it an accident?</summary>
    UnauthorizedOrgasm,
    /// <summary>A session that stopped without a clean end: what happened?</summary>
    InterruptedSession,
}

public enum FollowUpStatus { Open, Answered, Voided }

/// <summary>
/// A question she asks before anything else: what was observed, and the user's answer recorded
/// next to it. Consequences are written to the ledger when it is answered.
/// </summary>
public sealed class FollowUpRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public FollowUpKind Kind { get; set; }
    public FollowUpStatus Status { get; set; }
    public int? SessionId { get; set; }
    /// <summary>The tracker event observed (an orgasm), if any.</summary>
    public Guid? EventId { get; set; }
    /// <summary>When the observed thing happened.</summary>
    public DateTime OccurredAtUtc { get; set; }
    /// <summary>Commitments in force at that moment, as JSON.</summary>
    public string Commitments { get; set; } = "[]";
    /// <summary>A ledger denial window was in force.</summary>
    public bool DuringDenial { get; set; }
    /// <summary>What was observed, in plain words.</summary>
    public string Observed { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public string? Answer { get; set; }
    public DateTime? AnsweredAtUtc { get; set; }
}

/// <summary>
/// An offer definition in the library: shared defaults with personal overrides, edited like methods
/// and punishment endings. The domme fills in its parameters at the moment of a trade.
/// </summary>
public sealed class OfferDefinition : IDefinition<OfferDefinition>
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int? UserId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int? BaseId { get; set; }
    public int Id { get; set; }
    [MaxLength(128)]
    public string Name { get; set; } = "";
    /// <summary>Her words. Slots: [amount], [hold], [s], [what].</summary>
    [MaxLength(1000)]
    public string Wording { get; set; } = "";
    public OfferKind Kind { get; set; }
    public int MinAmount { get; set; }
    public int MaxAmount { get; set; }
    public int MinHold { get; set; }
    public int MaxHold { get; set; }
    public double BasePoints { get; set; }
    public double PointsPerUnit { get; set; }
    public double PointsPerHoldSecond { get; set; }
    public OfferFlavor Flavor { get; set; }
    /// <summary>Comma-separated moods that favour this offer.</summary>
    [MaxLength(128)]
    public string Moods { get; set; } = "";
    public bool ForOrgasm { get; set; }
    public bool ForRest { get; set; }
    public bool ForMercy { get; set; }
    public bool Enabled { get; set; } = true;

    public OfferDefinition AsDefinition(int id)
    {
        var copy = (OfferDefinition)MemberwiseClone();
        copy.Id = id;
        copy.UserId = null;
        copy.BaseId = null;
        return copy;
    }
}
