using AIDome2025.Data;
using AIDome2025.Services;

namespace AIDome.Persistence;

/// <summary>
/// Request-scoped facade over the account's persistent command center. It deliberately does
/// not implement IDisposable: disposing an HTTP scope must not unsubscribe the account's
/// timer/chat callbacks. GuidedRuntime alone owns the command center's lifetime.
/// </summary>
public sealed class BorrowedCommandCenter(CommandCenterService owner) : ICommandCenterService
{
    public event EventHandler? StateChanged { add => owner.StateChanged += value; remove => owner.StateChanged -= value; }
    public IReadOnlyList<ChatMessage> GetLatestMessages() => owner.GetLatestMessages();
    public IReadOnlyList<CommandActionDefinition> GetCurrentActions() => owner.GetCurrentActions();
    public ValueTask TriggerActionAsync(string actionId, CancellationToken cancellationToken = default) => owner.TriggerActionAsync(actionId, cancellationToken);
    public Task<SessionStatus> GetSessionStatusAsync(CancellationToken cancellationToken = default) => owner.GetSessionStatusAsync(cancellationToken);
    public ValueTask StartSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default) => owner.StartSessionAsync(parameters, cancellationToken);
    public ValueTask StartPunishmentSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default) => owner.StartPunishmentSessionAsync(parameters, cancellationToken);
    public ValueTask RecordCumRequestAsync(bool decision, CancellationToken cancellationToken = default) => owner.RecordCumRequestAsync(decision, cancellationToken);
    public OnlineSessionState? GetOnlineState() => owner.GetOnlineState();
    public Task<IReadOnlyList<InstructionStep>> GenerateDebugHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default) => owner.GenerateDebugHomeworkAsync(parameters, cancellationToken);
    public Task<IReadOnlyList<InstructionStep>> GenerateDebugPunishmentHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default) => owner.GenerateDebugPunishmentHomeworkAsync(parameters, cancellationToken);
    public Task<ResolvedDefaults> GetResolvedDefaultsAsync(CancellationToken cancellationToken = default) => owner.GetResolvedDefaultsAsync(cancellationToken);
    public ValueTask ReportInfractionAsync(InfractionType type, string? note = null, CancellationToken cancellationToken = default) => owner.ReportInfractionAsync(type, note, cancellationToken);
    public Task<IReadOnlyList<SessionRecord>> GetSessionHistoryAsync(int count = 10, CancellationToken cancellationToken = default) => owner.GetSessionHistoryAsync(count, cancellationToken);
    public int GetCurrentPoints() => owner.GetCurrentPoints();
    public ValueTask EndSessionAsync(CancellationToken cancellationToken = default) => owner.EndSessionAsync(cancellationToken);
    public ValueTask SubmitGameAnswerAsync(int answer, CancellationToken cancellationToken = default) => owner.SubmitGameAnswerAsync(answer, cancellationToken);
    public Task<PreviousSessionSuspicion?> CheckPreviousSessionAsync(CancellationToken cancellationToken = default) => owner.CheckPreviousSessionAsync(cancellationToken);
    public Task HandleConfessionAsync(bool confessed, CancellationToken cancellationToken = default) => owner.HandleConfessionAsync(confessed, cancellationToken);
    public Task<IReadOnlyList<PunishmentSummary>> GetActivePunishmentSummariesAsync(CancellationToken cancellationToken = default) => owner.GetActivePunishmentSummariesAsync(cancellationToken);
}
