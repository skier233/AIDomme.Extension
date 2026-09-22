using System;

namespace AIDome.Data;

public sealed class EdgeEntry
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public int StimMethodId { get; set; }
    public StimMethod? StimMethod { get; set; }
    public int Speed { get; set; }
    public TimeSpan HeldDuration { get; set; }
    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }
}
