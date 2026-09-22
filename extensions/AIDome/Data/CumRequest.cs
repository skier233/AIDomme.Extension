using System;

namespace AIDome.Data;

public sealed class CumRequest
{
    public int Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }

    public bool Decision { get; set; }
}
