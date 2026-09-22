namespace AIDome.Data;

/// <summary>A concrete offer the domme put to the user. Terms are snapshotted when proposed.</summary>
public sealed class OfferRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public string Name { get; set; } = "";
    public string Terms { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Target { get; set; }
    public int? DueAfterDays { get; set; }
    public DateTime? DueAtUtc { get; set; }
    /// <summary>Proposed, Accepted, Declined or Cancelled.</summary>
    public string Status { get; set; } = "Proposed";
    public int? OriginSessionId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? DecidedAtUtc { get; set; }
}

/// <summary>An agreement in force. Only an explicit acceptance creates one.</summary>
public sealed class CommitmentRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public Guid? OfferId { get; set; }
    public string Name { get; set; } = "";
    public string Terms { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal Target { get; set; }
    public decimal Progress { get; set; }
    public DateTime? DueAtUtc { get; set; }
    /// <summary>Active, Completed, Partial or Broken.</summary>
    public string Status { get; set; } = "Active";
    public DateTime AcceptedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
}

/// <summary>What the user reported about a commitment, recorded separately from observed progress.</summary>
public sealed class CommitmentReportRecord : IUserOwned
{
    public Guid Id { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public Guid CommitmentId { get; set; }
    public string Outcome { get; set; } = "";
    public int Degree { get; set; }
    public string? Note { get; set; }
    public DateTime ReportedAtUtc { get; set; }
}
