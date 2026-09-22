using System;
using System.ComponentModel.DataAnnotations;

namespace AIDome.Data;

public sealed class TuningComparisonEntry
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OwnerId { get; set; }
    public int Id { get; set; }

    [MaxLength(32)]
    public string Mode { get; set; } = string.Empty;

    [MaxLength(256)]
    public string WinnerKey { get; set; } = string.Empty;

    [MaxLength(256)]
    public string LoserKey { get; set; } = string.Empty;

    /// <summary>1 = barely prefer, 2 = prefer (default), 3 = strongly prefer. Legacy rows have 0 treated as 2.</summary>
    public int Strength { get; set; }

    public DateTime TimestampUtc { get; set; }
}
