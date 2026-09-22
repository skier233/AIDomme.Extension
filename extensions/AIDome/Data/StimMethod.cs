using System.ComponentModel.DataAnnotations;

namespace AIDome2025.Data;

/// <summary>Category of toy required (if any). Stored as int in the DB.</summary>
public enum ToyCategory
{
    None = 0,
    HeadVibrator = 1,
    Fleshlight = 2,
    Autoblow = 3,
    ProstateMassager = 4
}

public sealed class StimMethod
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int OwnerId { get; set; }
    public int Id { get; set; }

    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>1-10 scale. How intense the method is.</summary>
    public int Intensity { get; set; }

    /// <summary>1-10 scale. How teasing / sexually frustrating.</summary>
    public int Tease { get; set; }

    /// <summary>1-10 scale. How torturous the method is.</summary>
    public int TortLevel { get; set; }

    /// <summary>0-10 scale. How quickly this method brings the user close to orgasm.</summary>
    public int Closeness { get; set; }

    /// <summary>Whether lube is required / recommended.</summary>
    public bool RequiresLube { get; set; }

    /// <summary>What category of toy is needed (None = hands only).</summary>
    public ToyCategory ToyCategory { get; set; }

    /// <summary>Human-readable instruction text shown to the user.</summary>
    [MaxLength(512)]
    public string Description { get; set; } = string.Empty;

    /// <summary>Learned live-session point multiplier applied per second for timed steps.</summary>
    public double TimePointMultiplier { get; set; }

    /// <summary>Learned live-session point multiplier applied per edge for edge-count steps.</summary>
    public double EdgePointMultiplier { get; set; }

    public ICollection<EdgeEntry> Edges { get; set; } = new List<EdgeEntry>();
}
