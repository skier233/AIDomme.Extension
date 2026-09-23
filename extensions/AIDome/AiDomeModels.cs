using AIDome.Data;
using AIDome.Services;
using AIDome.Sessions;

namespace AIDome;

/// <summary>
/// The live snapshot returned by GET /state and every session mutation. The client polls it and
/// derives the countdown locally from <see cref="StepEndsAtUtc"/>.
/// </summary>
public sealed record AiDomeStateSnapshot(
    int UserId,
    Guid InstanceId,
    long Revision,
    bool SessionActive,
    SessionView? Session,
    DateTime? StepEndsAtUtc,
    AiDomeMediaCue? MediaCue,
    IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// The domme's media cue: which kind of Cove media to show, and a token that changes when she wants
/// something new. The page fetches a fresh random item of that kind whenever the token changes.
/// </summary>
public sealed record AiDomeMediaCue(string Kind, int Token);

/// <summary>A mode as offered on the setup screen.</summary>
public sealed record ModePresetView(SessionMode Mode, string Name, string Description, SessionContract Contract);

/// <summary>Setup-screen payload.</summary>
public sealed record AiDomeSetupSnapshot(
    ResolvedDefaults Defaults,
    IReadOnlyList<PunishmentSummary> ActivePunishments,
    IReadOnlyList<ModePresetView> Modes,
    IReadOnlyList<ChatMessage> Messages);

public sealed record StartSessionRequest(SessionContract Contract);
public sealed record HomeworkRequest(SessionContract Contract, bool Punishment = false);

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
public sealed record AiDomeInfractionRequest(string Type, string? Note = null);
public sealed record AiDomeTtsRequest(string Text);
