using System;
using System.ComponentModel.DataAnnotations;

namespace AIDome2025.Data;

public sealed class TuningRatingEntry
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OwnerId { get; set; }
    public int Id { get; set; }

    [MaxLength(256)]
    public string CandidateKey { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Mode { get; set; } = string.Empty;

    [MaxLength(64)]
    public string Group { get; set; } = string.Empty;

    [MaxLength(128)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1024)]
    public string Description { get; set; } = string.Empty;

    public double SeedRating { get; set; }

    public double Rating { get; set; }

    public int Wins { get; set; }

    public int Losses { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
