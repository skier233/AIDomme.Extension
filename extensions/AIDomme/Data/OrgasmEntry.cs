using System;

namespace AIDomme.Data;

public sealed class OrgasmEntry : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public bool Allowed { get; set; }
    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }
}
