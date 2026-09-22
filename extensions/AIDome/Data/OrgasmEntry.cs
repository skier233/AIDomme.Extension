using System;

namespace AIDome2025.Data;

public sealed class OrgasmEntry
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Allowed { get; set; }
    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }
}
