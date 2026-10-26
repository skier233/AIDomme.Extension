namespace AIDomme.Data;

public sealed class GuidedEventReceipt : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public Guid EventId { get; set; }
    public int SessionId { get; set; }
    public SessionRecord Session { get; set; } = null!;
    public string Kind { get; set; } = "";
    /// <summary>The guided step that was current when the event was recorded, when known.</summary>
    public int? StepNumber { get; set; }
    public int ActivityId { get; set; }
    public int? InfractionId { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}
