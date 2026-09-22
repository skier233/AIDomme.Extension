using AIDome.Data;
using AIDome.Services;

namespace AIDome;

/// <summary>
/// Full live-session snapshot returned by GET /state and by every mutating action
/// endpoint. The React frontend polls this while a session is active and derives the
/// step countdown locally from <see cref="StepEndsAtUtc"/> (no server-side ticking).
/// </summary>
public sealed record AiDomeStateSnapshot(
    int UserId,
    Guid InstanceId,
    long Revision,
    int PolicyRevision,
    SessionPolicy Policy,
    bool SessionActive,
    DateTime? LastInteractionUtc,
    int Points,
    OnlineSessionState? Online,
    DateTime? StepEndsAtUtc,
    AiDomeMediaCue? MediaCue,
    IReadOnlyList<ChatMessage> Messages,
    IReadOnlyList<CommandActionDefinition> Actions);

/// <summary>
/// The domme's directive to the media player: which kind of Cove media to show and a
/// monotonically increasing token. The frontend fetches a fresh random item of <see cref="Kind"/>
/// whenever <see cref="Token"/> changes, so media follows the session without the backend needing
/// the user's media token. Null = nothing playing.
/// </summary>
public sealed record AiDomeMediaCue(string Kind, int Token);

/// <summary>Setup-screen payload, fetched once when no session is active.</summary>
public sealed record AiDomeSetupSnapshot(
    ResolvedDefaults Defaults,
    IReadOnlyList<PunishmentSummary> ActivePunishments,
    PreviousSessionSuspicion? Suspicion,
    IReadOnlyList<ChatMessage> Messages);

/// <summary>User-facing extension settings, persisted in the Cove extension store.</summary>
public sealed record AiDomeSettings(string TtsBaseUrl, string TtsVoice, int TtsTimeoutSeconds)
{
    public static AiDomeSettings From(TtsOptions o) => new(o.BaseUrl ?? "", o.Voice ?? "mia", o.TimeoutSeconds);

    public void ApplyTo(TtsOptions o)
    {
        o.BaseUrl = (TtsBaseUrl ?? "").Trim();
        o.Voice = string.IsNullOrWhiteSpace(TtsVoice) ? "mia" : TtsVoice.Trim();
        o.TimeoutSeconds = TtsTimeoutSeconds <= 0 ? 30 : TtsTimeoutSeconds;
    }
}

// ── Request bodies ──
public sealed record AiDomeGameAnswerRequest(int Answer);
public sealed record AiDomeInfractionRequest(string Type, string? Note = null);
public sealed record AiDomeCumRequest(bool Decision);
public sealed record AiDomeConfessionRequest(bool Confessed);
public sealed record AiDomeTtsRequest(string Text);
