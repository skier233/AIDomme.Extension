namespace AIDome2025.Data;

public sealed class GuidedEventReceipt
{
    public int Id { get; set; }
    public Guid EventId { get; set; }
    public int SessionId { get; set; }
    public SessionRecord Session { get; set; } = null!;
    public string Kind { get; set; } = "";
    public int ActivityId { get; set; }
    public int? InfractionId { get; set; }
    public DateTime? DeliveredAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
}
