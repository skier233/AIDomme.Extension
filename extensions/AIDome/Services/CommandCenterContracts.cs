using System;
using System.Collections.Generic;
using System.Threading;
using AIDome2025.Data;

namespace AIDome2025.Services;

public record ChatMessage(Guid Id, DateTime Timestamp, string Sender, string Content);

public record CommandActionDefinition(
    string Id,
    string Label,
    string? Hotkey,
    string Description
);

public record SessionStatus(bool IsActive, DateTime? LastInteractionUtc);

public class SessionParameters
{
    public SessionPolicy Policy { get; set; } = new();
    public int Intensity { get; set; }
    public int Length { get; set; }
    public int Denial { get; set; }
    public int Kink { get; set; }
    public int Mean { get; set; }
    public bool OnlyGamesMode { get; set; }

    // Toy / kink toggles
    public bool AllowToys { get; set; }
    public bool AllowVibrator { get; set; }
    public bool AllowFleshlight { get; set; }
    public bool AllowAutoblow { get; set; }
    public bool AllowProstate { get; set; }
    public bool AllowAnal { get; set; }
    public bool AllowBondage { get; set; }
    public bool AllowCEI { get; set; }
    public bool AllowSelfOral { get; set; }

    // Anti-cheat: debug sessions skip the suspected-orgasm tracking
    public bool IsDebugSession { get; set; }

    public SessionParameters() { }

    public SessionParameters(int intensity, int length, int denial, int kink, int mean)
    {
        Intensity = intensity;
        Length = length;
        Denial = denial;
        Kink = kink;
        Mean = mean;
    }
}

/// <summary>Auto-resolved defaults from session history (for -1 params).</summary>
public record ResolvedDefaults(int Intensity, int Length, int Denial, int Kink, int Mean);

public interface ICommandCenterService
{
    event EventHandler? StateChanged;

    IReadOnlyList<ChatMessage> GetLatestMessages();

    IReadOnlyList<CommandActionDefinition> GetCurrentActions();

    ValueTask TriggerActionAsync(string actionId, CancellationToken cancellationToken = default);

    Task<SessionStatus> GetSessionStatusAsync(CancellationToken cancellationToken = default);

    ValueTask StartSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default);

    ValueTask StartPunishmentSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default);
    
    ValueTask RecordCumRequestAsync(bool decision, CancellationToken cancellationToken = default);

    OnlineSessionState? GetOnlineState();

    Task<IReadOnlyList<InstructionStep>> GenerateDebugHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InstructionStep>> GenerateDebugPunishmentHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default);

    /// <summary>Calculate auto-resolved defaults from recent session history.</summary>
    Task<ResolvedDefaults> GetResolvedDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>Report an infraction during the current session.</summary>
    ValueTask ReportInfractionAsync(InfractionType type, string? note = null, CancellationToken cancellationToken = default);

    /// <summary>Get session history for display.</summary>
    Task<IReadOnlyList<SessionRecord>> GetSessionHistoryAsync(int count = 10, CancellationToken cancellationToken = default);

    /// <summary>Current session points.</summary>
    int GetCurrentPoints();

    /// <summary>Ends the active session and returns to setup state.</summary>
    ValueTask EndSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Submit the user's answer to an active game challenge.</summary>
    ValueTask SubmitGameAnswerAsync(int answer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the previous session for suspicious abandonment.
    /// Returns a description if a confession is needed, or null if clean.
    /// </summary>
    Task<PreviousSessionSuspicion?> CheckPreviousSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// User responds to the confession prompt.
    /// If confessed=true, an infraction is recorded and punishment assigned.
    /// If confessed=false, the suspected flag is cleared.
    /// </summary>
    Task HandleConfessionAsync(bool confessed, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of active punishments for display on the setup screen.
    /// </summary>
    Task<IReadOnlyList<PunishmentSummary>> GetActivePunishmentSummariesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Info about a suspicious previous session requiring confession.</summary>
public sealed class PreviousSessionSuspicion
{
    public int SessionId { get; init; }
    public DateTime SessionDate { get; init; }
    public SessionOutcome Outcome { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>Summary of an active punishment for UI display.</summary>
public sealed class PunishmentSummary
{
    public int Id { get; init; }
    public string Description { get; init; } = string.Empty;
    public PunishmentCategory Category { get; init; }
    public int Tier { get; init; }
    public int RemainingCount { get; init; }
    public DateTime? DenialEndsAtUtc { get; init; }
}
