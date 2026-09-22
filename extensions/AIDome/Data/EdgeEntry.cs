using System;

namespace AIDome.Data;

public sealed class EdgeEntry : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int StimMethodId { get; set; }
    public StimMethod? StimMethod { get; set; }
    public int Speed { get; set; }
    public TimeSpan HeldDuration { get; set; }
    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }
}
