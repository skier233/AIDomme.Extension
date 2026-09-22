namespace AIDome2025.Services;

/// <summary>Accepted, independent controls. Presets are a UI convenience only.</summary>
public sealed record SessionPolicy
{
    public string Guidance { get; init; } = "Detailed";
    public string Pacing { get; init; } = "Engine";
    public string Media { get; init; } = "User";
    public string Permission { get; init; } = "EngineFinish";
    public bool GamesEnabled { get; init; } = true;
    public int[] AllowedMethodIds { get; init; } = [];
    public bool CanChangeDuringSession { get; init; }

    public void Validate()
    {
        if (Guidance is not ("Detailed" or "Simple" or "Permission")
            || Pacing is not ("Engine" or "Free") || Media is not ("User" or "Engine")
            || Permission is not ("EngineFinish" or "RequestsOnly")
            || AllowedMethodIds is null || AllowedMethodIds.Length > 200 || AllowedMethodIds.Any(x => x <= 0))
            throw new ArgumentException("Invalid session settings.");
        if (Guidance == "Permission" && (Pacing != "Free" || Permission != "RequestsOnly" || GamesEnabled))
            throw new ArgumentException("Permission guidance currently uses free pacing, request-only permission and no scheduled games. Permission challenges arrive in phase 5.");
    }
}
