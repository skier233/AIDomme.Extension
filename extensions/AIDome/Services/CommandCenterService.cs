using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIDome2025.Utils;
using AIDome2025.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIDome2025.Services;

public sealed class CommandCenterService : ICommandCenterService, IHostedService, IDisposable
{
    private const int MaxMessages = 200;
    private static readonly IReadOnlyList<CommandActionDefinition> StaticActions =
    [
        new("edged", "Edged", "E", "Log an edge event"),
        new("done", "Complete Step", "D", "Mark the current instruction step as done"),
        new("orgasm", "Orgasm", "O", "Record an orgasm outcome"),
        new("beg-to-cum", "Beg To Cum", "B", "Log a request to cum"),
        new("beg-to-stop", "Beg To Stop", "S", "Beg to stop the current action")
    ];
    private static readonly TimeSpan MinHoldDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxHoldDuration = TimeSpan.FromSeconds(45);
    private const int MinEdgeSpeed = 1;
    private const int MaxEdgeSpeed = 10;

    private readonly object _gate = new();
    private readonly List<ChatMessage> _messages = new();
    private readonly List<CommandActionDefinition> _actions = new(StaticActions);
    private readonly ILogger<CommandCenterService> _logger;
    private readonly IInteractionManager _interactionManager;
    private readonly IDbContextFactory<CommandCenterDbContext> _dbContextFactory;
    private readonly IOnlineSessionEngine _onlineEngine;
    private readonly IInstructionEngine _instructionEngine;
    private readonly IGameEngine _gameEngine;

    private CancellationTokenSource? _cts;

    public CommandCenterService(
        ILogger<CommandCenterService> logger,
        IInteractionManager interactionManager,
        IDbContextFactory<CommandCenterDbContext> dbContextFactory,
        IOnlineSessionEngine onlineEngine,
        IInstructionEngine instructionEngine,
        IGameEngine gameEngine)
    {
        _logger = logger;
        _interactionManager = interactionManager;
        _dbContextFactory = dbContextFactory;
        _onlineEngine = onlineEngine;
        _instructionEngine = instructionEngine;
        _gameEngine = gameEngine;

        _onlineEngine.StepTimerElapsed += OnStepTimerElapsed;
    }

    public async Task<SessionStatus> GetSessionStatusAsync(CancellationToken cancellationToken = default)
    {
        // Engine must be actively running — don't restore from DB across restarts
        if (!_onlineEngine.HasActiveSession)
            return new SessionStatus(false, null);

        var session = await _interactionManager.GetLatestSessionAsync(cancellationToken);
        if (session is null)
            return new SessionStatus(false, null);

        return new SessionStatus(true, session.LastInteractionUtc);
    }

    public async ValueTask StartSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default)
    {
        if (_onlineEngine.HasActiveSession)
        {
            _onlineEngine.EndSession();
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Previous session ended before starting new one."));
        }

        if (_onlineEngine is OnlineSessionEngine owned) await owned.PreparePolicyAsync(parameters, cancellationToken);
        var session = await _interactionManager.StartSessionAsync(parameters, cancellationToken);
        _consumedTrackerEvents.Clear();
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", $"Session {session.Id} started (intensity={parameters.Intensity}, length={parameters.Length}, denial={parameters.Denial}, kink={parameters.Kink}, mean={parameters.Mean})."));
        _logger.LogInformation("Session {SessionId} started", session.Id);

        var firstDirective = await _onlineEngine.BeginSessionAsync(session.Id, parameters.OnlyGamesMode, cancellationToken);
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(firstDirective)));

        RaiseStateChanged();
    }

    public async ValueTask StartPunishmentSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default)
    {
        if (_onlineEngine.HasActiveSession)
        {
            _onlineEngine.EndSession();
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Previous session ended before starting new one."));
        }

        if (_onlineEngine is OnlineSessionEngine owned) await owned.PreparePolicyAsync(parameters, cancellationToken);
        var session = await _interactionManager.StartSessionAsync(parameters, cancellationToken);
        _consumedTrackerEvents.Clear();
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", $"Punishment session {session.Id} started (intensity={parameters.Intensity}, length={parameters.Length}, denial={parameters.Denial}, kink={parameters.Kink}, mean={parameters.Mean})."));
        _logger.LogInformation("Punishment session {SessionId} started", session.Id);

        var firstDirective = await _onlineEngine.BeginPunishmentSessionAsync(session.Id, parameters.OnlyGamesMode, cancellationToken);
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(firstDirective)));

        RaiseStateChanged();
    }

    public event EventHandler? StateChanged;

    public IReadOnlyList<ChatMessage> GetLatestMessages()
    {
        lock (_gate)
        {
            return _messages.ToList();
        }
    }

    public IReadOnlyList<CommandActionDefinition> GetCurrentActions()
    {
        lock (_gate)
        {
            return _actions.ToList();
        }
    }

    private readonly HashSet<Guid> _consumedTrackerEvents = new();
    public (int Speed, TimeSpan HoldDuration) GetTrackerEdgeParameters() => ComputeEdgeParameters();
    public async Task ConsumeTrackerEventAsync(Guid eventId, string kind, CancellationToken ct, bool? recordedOrgasmAllowed = null)
    {
        // Called under the account runtime gate; retries must not advance the engine twice.
        if (!_consumedTrackerEvents.Add(eventId)) return;
        await TriggerActionCoreAsync(kind == "edge" ? "edged" : "orgasm", true, ct, recordedOrgasmAllowed);
    }
    public void ObserveEarlierTrackerEdge(Guid eventId)
    {
        if (!_consumedTrackerEvents.Add(eventId)) return;
        if (_onlineEngine is OnlineSessionEngine owned) owned.ObserveEarlierEdge();
        CompanionNotice("An edge from an earlier step was recorded. Session counts are retained; the current target was not advanced.");
    }
    public void StopAfterTrackingCorrection()
    {
        _onlineEngine.EndSession();
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Guidance stopped after a tracking correction. Recorded counts were corrected; start a new session to continue."));
        RaiseStateChanged();
    }
    public ValueTask TriggerActionAsync(string actionId, CancellationToken cancellationToken = default)
        => TriggerActionCoreAsync(actionId, false, cancellationToken);
    private async ValueTask TriggerActionCoreAsync(string actionId, bool alreadyRecorded, CancellationToken cancellationToken, bool? recordedOrgasmAllowed = null)
    {
        if (_onlineEngine.GetCurrentState().IsPaused) throw new InvalidOperationException("Resume the session before using guided actions.");
        if (actionId == "done" && _onlineEngine.HasActiveSession)
        {
            var state = _onlineEngine.GetCurrentState();
            if (!state.IsLubePause && state.CurrentDirective?.Kind != InstructionKind.Finish)
                throw new InvalidOperationException("This step advances through its timer, edge target or challenge result.");
        }
        CommandActionDefinition? action;
        lock (_gate)
        {
            action = _actions.FirstOrDefault(a => string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));
        }

        if (action is null)
        {
            _logger.LogWarning("Received trigger for unknown action {ActionId}", actionId);
            return;
        }

        switch (action.Id)
        {
            case "edged":
                var (edgeSpeed, edgeHold) = ComputeEdgeParameters();

                // Process in engine first, then persist to DB
                SessionDirective? edgeDirective = null;
                if (_onlineEngine.HasActiveSession)
                {
                    edgeDirective = await _onlineEngine.RecordEdgeAsync(cancellationToken);
                }

                if (!alreadyRecorded)
                    await _interactionManager.RecordEdgeAsync(edgeSpeed, edgeHold, cancellationToken);
                AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", $"Edge logged (speed {edgeSpeed}, hold {edgeHold.TotalSeconds:N0}s)."));

                if (edgeDirective is not null)
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(edgeDirective)));
                break;

            case "done":
            {
                if (!_onlineEngine.HasActiveSession)
                {
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "No active session to advance."));
                    break;
                }
                var preState = _onlineEngine.GetCurrentState();
                if (preState.ActiveGameChallengeId is not null && !preState.IsLubePause)
                {
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Finish the active game before completing the step."));
                    RaiseStateChanged();
                    break;
                }

                if (preState.CurrentDirective?.Phase == SessionPhase.Finishing
                    && preState.CurrentDirective.Kind != InstructionKind.Finish)
                {
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Use Orgasm when you actually finish. If the timer expires, you'll be denied."));
                    RaiseStateChanged();
                    break;
                }

                var nextDirective = await _onlineEngine.CompleteStepAsync(cancellationToken);
                AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(nextDirective)));
                _logger.LogInformation("Step completed. Next: {Text}", nextDirective.Text);

                // If session just ended via CompleteStep, persist the outcome
                var postState = _onlineEngine.GetCurrentState();
                if (postState.IsFinished && postState.Outcome != SessionOutcome.Unknown)
                {
                    await FinalizeSessionOutcomeAsync(postState.SessionId, postState.Outcome, cancellationToken);
                }
                break;
            }

            case "orgasm":
                if (_onlineEngine.HasActiveSession)
                {
                    var orgState = _onlineEngine.GetCurrentState();
                    if (recordedOrgasmAllowed ?? orgState.Phase == SessionPhase.Finishing)
                    {
                        // Permission was already granted, this is the expected orgasm
                        if (!alreadyRecorded) await _interactionManager.RecordOrgasmAsync(allowed: true, cancellationToken);
                        _onlineEngine.EndSession(SessionOutcome.AllowedOrgasm);
                        await FinalizeSessionOutcomeAsync(orgState.SessionId, SessionOutcome.AllowedOrgasm, cancellationToken);
                        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", "Good. Session complete."));
                        _logger.LogInformation("Allowed orgasm recorded for session {SessionId}", orgState.SessionId);
                    }
                    else
                    {
                        // Unauthorized orgasm mid-session — scold and retaliate
                        if (!alreadyRecorded)
                        {
                            await _interactionManager.RecordOrgasmAsync(allowed: false, cancellationToken);
                            await _interactionManager.RecordInfractionAsync(InfractionType.UnauthorizedOrgasm, null, cancellationToken);
                        }
                        _onlineEngine.NotifyEvent(SessionEvent.OrgasmReported);
                        var retaliationDirective = await _onlineEngine.CompleteStepAsync(cancellationToken);
                        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", retaliationDirective.Text));
                        _logger.LogInformation("Unauthorized orgasm recorded for session {SessionId}", orgState.SessionId);
                    }
                }
                break;

            case "beg-to-cum":
            {
                var sessionId = await _interactionManager.GetCurrentSessionId(cancellationToken);
                bool decision = await RequestCum(cancellationToken, sessionId);
                var cumrequest = await _interactionManager.RecordCumRequestAsync(decision, cancellationToken);

                if (decision)
                {
                    var begState = _onlineEngine.GetCurrentState();
                    if (_onlineEngine is OnlineSessionEngine owned) owned.GrantPermission();
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                        "Permission granted. You may cum."));
                }
                else
                {
                    _onlineEngine.NotifyEvent(SessionEvent.BegDenied);
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                        DommeText.Pick(DommeText.BegCumDenied)));
                }

                _logger.LogInformation("Beg-to-cum for session {SessionId}: {Decision}", cumrequest.SessionId, cumrequest.Decision);
                break;
            }

            case "beg-to-stop":
            {
                if (!_onlineEngine.HasActiveSession)
                {
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "No active session."));
                    break;
                }

                var currentState = _onlineEngine.GetCurrentState();
                // Can't beg to stop during rest — you're already resting
                if (currentState.CurrentDirective?.Kind == InstructionKind.Rest)
                {
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", "You're already resting. Don't push it."));
                    break;
                }

                // Mean + arousal determine stop chance: lower mean + lower arousal = higher chance
                int meanLevel = currentState.Arousal > 70 ? 2 : 0; // harder to stop when aroused
                bool grantStop = Random.Shared.Next(10) < (3 - meanLevel); // ~30% base, less when aroused

                if (grantStop)
                {
                    _onlineEngine.NotifyEvent(SessionEvent.BegStopGranted);
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                        DommeText.Pick(DommeText.BegStopGranted)));
                    // Advance to the next step (which will be a rest since stop was granted)
                    var restDirective = await _onlineEngine.CompleteStepAsync(cancellationToken);
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", restDirective.Text));
                }
                else
                {
                    _onlineEngine.NotifyEvent(SessionEvent.BegStopDenied);
                    AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                        DommeText.Pick(DommeText.BegStopDenied)));
                }

                _logger.LogInformation("Beg-to-stop: granted={Grant}", grantStop);
                break;
            }

            default:
                _logger.LogWarning("Received unhandled action {Action}", action.Id);
                break;
        }
    }

    public async Task<double> GetCumChance(CancellationToken cancellationToken, int sessionId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = DateTime.UtcNow;

        // Use the session's Denial parameter; fall back to a moderate default.
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        int denyLevel = session is not null && session.Denial >= 0 ? session.Denial : 5;

        // Last orgasm overall
        var lastOrgasm = await db.Orgasms.OrderByDescending(o => o.TimestampUtc).FirstOrDefaultAsync(cancellationToken);
        TimeSpan timeSinceLastOrgasm = lastOrgasm is not null
            ? now - lastOrgasm.TimestampUtc
            : TimeSpan.FromDays(1.5);

        // Cum requests
        var cumRequestsInSession = await db.CumRequests.CountAsync(c => c.SessionId == sessionId, cancellationToken);
        var startOfToday = now.Date;
        var endOfToday = startOfToday.AddDays(1);
        var cumRequestsToday = await db.CumRequests.CountAsync(c => c.TimestampUtc >= startOfToday && c.TimestampUtc < endOfToday, cancellationToken);

        // Edges
        var edgesInSession = await db.Edges.CountAsync(e => e.SessionId == sessionId, cancellationToken);
        var edgesToday = await db.Edges.CountAsync(e => e.TimestampUtc >= startOfToday && e.TimestampUtc < endOfToday, cancellationToken);
        var edgesSinceLastOrgasm = 0;
        if (lastOrgasm is not null)
        {
            edgesSinceLastOrgasm = await db.Edges.CountAsync(e => e.TimestampUtc > lastOrgasm.TimestampUtc, cancellationToken);
        }

        if (_dbContextFactory is AIDome.Persistence.BoundGuidedFactory owned)
        {
            var metrics = await owned.ReadTrackerMetricsAsync(now, cancellationToken);
            // Passive browsing contributes too. An unknown last orgasm stays unknown rather
            // than fabricating the legacy 1.5-day interval; the formula uses its neutral floor.
            timeSinceLastOrgasm = metrics.LastOrgasmAtUtc is DateTime last ? now - last : TimeSpan.Zero;
            edgesToday = metrics.EdgesToday;
            edgesSinceLastOrgasm = metrics.EdgesSinceLastOrgasm;
        }

        double A = Math.Max(timeSinceLastOrgasm.TotalHours, 0.1); // avoid zero
        var B = edgesInSession;
        var C = edgesToday;
        var D = edgesSinceLastOrgasm;
        var E = cumRequestsInSession;
        var F = cumRequestsToday;
        var G = denyLevel;

        var raw = CumChanceFormula(A, B, C, D, E, F, G);

        // Clamp to 0–100 and guard NaN/Infinity
        if (double.IsNaN(raw) || double.IsInfinity(raw))
        {
            _logger.LogWarning("Cum chance formula returned {Raw} — falling back to 5%", raw);
            return 5.0;
        }

        return Math.Clamp(raw, 0.0, 100.0);
    }

    private static double CumChanceFormula(double A, double B, double C, double D, double E, double F, double G)
    {
        // Guard denominators against zero
        double ratioA = (A + 40.859) / A; // A is ≥ 0.1, safe
        double innerDenom = D - ((67.021 - ((-17.106) * F)) / ratioA);
        if (Math.Abs(innerDenom) < 0.001)
            innerDenom = 0.001;

        double outerDenom = (202.823 - ((15.937 * Math.Pow(Math.Max(C, 0), 0.596)) - (15.937 * F))) / A;
        if (Math.Abs(outerDenom) < 0.001)
            outerDenom = 0.001;

        return ((58.035 - (7.294 * E)) - (((153.136 - (21.993 * E)) - 7.356) / innerDenom)) / outerDenom;
    }

    public async Task<bool> RequestCum(CancellationToken cancellationToken, int sessionId)
    {
        double cumChance = await GetCumChance(cancellationToken, sessionId);
        _logger.LogInformation("Cum chance for session {SessionId}: {CumChance:N1}%", sessionId, cumChance);
        return RandomHelper.PercentChance(cumChance);
    }

    public async ValueTask RecordCumRequestAsync(bool decision, CancellationToken cancellationToken = default)
    {
        var req = await _interactionManager.RecordCumRequestAsync(decision, cancellationToken);
        AddMessage(new ChatMessage(Guid.NewGuid(), req.TimestampUtc, "Manager", $"Cum request recorded (session {req.SessionId}, decision: {req.Decision})."));
        _logger.LogInformation("Cum request recorded for session {SessionId} (decision: {Decision})", req.SessionId, req.Decision);
    }

    public OnlineSessionState? GetOnlineState()
    {
        if (!_onlineEngine.HasActiveSession) return null;
        return _onlineEngine.GetCurrentState();
    }

    public async Task<IReadOnlyList<InstructionStep>> GenerateDebugHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default)
    {
        var newSession = await _interactionManager.StartSessionAsync(parameters, cancellationToken);
        int sessionId = newSession.Id;
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "System", $"Created session {sessionId} for homework."));

        var steps = await _instructionEngine.GeneratePlanAsync(sessionId, cancellationToken);
        foreach (var step in steps)
        {
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", $"Homework: {step.Text}"));
        }
        RaiseStateChanged();
        return steps;
    }

    public async Task<IReadOnlyList<InstructionStep>> GenerateDebugPunishmentHomeworkAsync(SessionParameters parameters, CancellationToken cancellationToken = default)
    {
        var newSession = await _interactionManager.StartSessionAsync(parameters, cancellationToken);
        int sessionId = newSession.Id;
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "System", $"Created session {sessionId} for punishment homework."));

        var steps = await _instructionEngine.GeneratePunishmentPlanAsync(sessionId, cancellationToken);
        foreach (var step in steps)
        {
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", $"Punishment: {step.Text}"));
        }
        RaiseStateChanged();
        return steps;
    }

    public async Task<ResolvedDefaults> GetResolvedDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var recent = await db.Sessions
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (recent.Count == 0)
            return new ResolvedDefaults(5, 5, 5, 5, 5);

        int avgI = (int)Math.Round(recent.Average(s => s.Intensity > 0 ? s.Intensity : 5));
        int avgL = (int)Math.Round(recent.Average(s => s.Length > 0 ? s.Length : 5));
        int avgD = (int)Math.Round(recent.Average(s => s.Denial > 0 ? s.Denial : 5));
        int avgK = (int)Math.Round(recent.Average(s => s.Kink > 0 ? s.Kink : 5));
        int avgM = (int)Math.Round(recent.Average(s => s.Mean > 0 ? s.Mean : 5));
        return new ResolvedDefaults(
            Math.Clamp(avgI, 1, 10),
            Math.Clamp(avgL, 1, 10),
            Math.Clamp(avgD, 1, 10),
            Math.Clamp(avgK, 1, 10),
            Math.Clamp(avgM, 1, 10));
    }

    public async ValueTask ReportInfractionAsync(InfractionType type, string? note = null, CancellationToken cancellationToken = default)
    {
        if (_onlineEngine.GetCurrentState().IsPaused) throw new InvalidOperationException("Resume the session before reporting a guided infraction.");
        var entry = await _interactionManager.RecordInfractionAsync(type, note, cancellationToken);
        string label = type switch
        {
            InfractionType.UnauthorizedOrgasm => "Unauthorized orgasm",
            InfractionType.RuinedOrgasm => "Ruined orgasm",
            InfractionType.Disobedience => "Disobedience",
            InfractionType.GameFailure => "Game failure",
            _ => type.ToString()
        };
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "System", $"Infraction recorded: {label}"));

        if (_onlineEngine.HasActiveSession)
        {
            var evt = type switch
            {
                InfractionType.RuinedOrgasm => SessionEvent.RuinedOrgasmReported,
                InfractionType.UnauthorizedOrgasm => SessionEvent.OrgasmReported,
                _ => SessionEvent.InfractionReported,
            };
            _onlineEngine.NotifyEvent(evt);
            var retaliationDirective = await _onlineEngine.CompleteStepAsync(cancellationToken);
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", retaliationDirective.Text));
        }
        RaiseStateChanged();
    }

    public async Task<IReadOnlyList<SessionRecord>> GetSessionHistoryAsync(int count = 10, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Sessions
            .OrderByDescending(s => s.StartedAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
    }

    public int GetCurrentPoints()
    {
        var state = _onlineEngine.HasActiveSession ? _onlineEngine.GetCurrentState() : null;
        return state?.Points ?? 0;
    }

    public async ValueTask EndSessionAsync(CancellationToken cancellationToken = default)
    {
        if (_onlineEngine.HasActiveSession)
        {
            var state = _onlineEngine.GetCurrentState();
            _onlineEngine.EndSession(SessionOutcome.Unknown);
            // Manual end → outcome unknown, suspicious flag stays
            await FinalizeSessionOutcomeAsync(state.SessionId, SessionOutcome.Unknown, cancellationToken);
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "Session ended manually."));
        }
        RaiseStateChanged();
    }

    public async ValueTask SubmitGameAnswerAsync(int answer, CancellationToken cancellationToken = default)
    {
        if (_onlineEngine.GetCurrentState().IsPaused) throw new InvalidOperationException("Resume the session before answering a game.");
        if (!_onlineEngine.HasActiveSession)
        {
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "No active session."));
            RaiseStateChanged();
            return;
        }

        var state = _onlineEngine.GetCurrentState();
        if (state.ActiveGameChallengeId is not int challengeId)
        {
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", "No active game to answer."));
            RaiseStateChanged();
            return;
        }

        // Special handling for RerollGamble: reroll keeps the game active
        if (state.ActiveGameType == GameType.RerollGamble && answer == 1)
        {
            int maxOutcome = 5;
            int newOutcome = Random.Shared.Next(1, maxOutcome + 1);
            var rerollDirective = _onlineEngine.HandleReroll(newOutcome);
            if (rerollDirective is not null)
            {
                AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", rerollDirective.Text));
                RaiseStateChanged();
                return;
            }
        }

        // Submit the answer to the game engine (DB)
        var challenge = await _gameEngine.SubmitAnswerAsync(challengeId, answer, cancellationToken);

        bool won = challenge.Won == true;
        string resultText = won
            ? challenge.SuccessReward
            : challenge.FailureConsequence;

        if (state.ActiveGameType == GameType.EdgeRush)
        {
            int actualCount = state.ActiveGameRange;
            bool truthful = answer == actualCount;
            won = truthful && actualCount >= challenge.CorrectAnswer;
            resultText = won
                ? $"Time's up. You reported {actualCount} edge(s), and that clears the target of {challenge.CorrectAnswer}."
                : truthful
                    ? $"Time's up. You only made {actualCount} edge(s). {challenge.FailureConsequence}"
                    : $"Too late to bluff. You reported {answer}, but you actually logged {actualCount}. That counts as a loss.";
        }

        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", resultText));

        // Resolve the game in the online engine (adjusts points/retaliation) and get next directive
        var nextDirective = _onlineEngine.ResolveGame(won);
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(nextDirective)));

        _logger.LogInformation("Game {Id} answered={Answer}, won={Won}", challengeId, answer, won);
        RaiseStateChanged();
    }

    public async Task<PreviousSessionSuspicion?> CheckPreviousSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var prev = await db.Sessions
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (prev is null) return null;
        if (prev.IsDebugSession) return null;

        // Check if the previous session was abandoned or suspected
        if (prev.SuspectedUnauthorizedOrgasm)
        {
            string msg = prev.Outcome == SessionOutcome.Unknown
                ? "Your last session ended without a proper conclusion. Did you cum without permission?"
                : "I have reason to believe you came without permission last session. Did you?";

            return new PreviousSessionSuspicion
            {
                SessionId = prev.Id,
                SessionDate = prev.StartedAtUtc,
                Outcome = prev.Outcome,
                Message = msg
            };
        }

        return null;
    }

    public async Task HandleConfessionAsync(bool confessed, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var prev = await db.Sessions
            .OrderByDescending(s => s.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (prev is null) return;

        if (confessed)
        {
            // Record the infraction against the previous session
            var infraction = new InfractionEntry
            {
                TimestampUtc = DateTime.UtcNow,
                SessionId = prev.Id,
                Type = InfractionType.UnauthorizedOrgasm,
                Notes = "Confessed during next session start."
            };
            db.Infractions.Add(infraction);

            prev.Outcome = SessionOutcome.UnauthorizedOrgasm;
            prev.SuspectedUnauthorizedOrgasm = false;

            // Determine punishment tier
            int recentInfractions = await db.Infractions
                .CountAsync(i => i.TimestampUtc > DateTime.UtcNow.AddDays(-7), cancellationToken);
            int baseTier = 4; // UnauthorizedOrgasm base severity
            int meanBoost = prev.Mean > 0 ? prev.Mean / 3 : 1;
            int escalation = Math.Max(0, recentInfractions - 1);
            int tier = Math.Clamp(baseTier + meanBoost + escalation, 1, 8);

            // Create a denial punishment
            int denialDays = tier switch
            {
                <= 2 => 3,
                <= 4 => 3 + tier,
                <= 6 => 5 + tier,
                _ => 7 + tier
            };

            var punishment = new PunishmentEntry
            {
                IssuedAtUtc = DateTime.UtcNow,
                SessionId = prev.Id,
                Category = PunishmentCategory.Denial,
                Tier = tier,
                Description = $"Denial for {denialDays} days for unauthorized orgasm (confessed). Tier {tier}.",
                RemainingCount = 0,
                DenialEndsAtUtc = DateTime.UtcNow.AddDays(denialDays)
            };
            db.Punishments.Add(punishment);
            infraction.PunishmentAssigned = true;

            await db.SaveChangesAsync(cancellationToken);

            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                DommeText.Pick(DommeText.ConfessionPunishment)));
            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "System",
                $"Punishment assigned: {punishment.Description}"));
            _logger.LogInformation("Confession recorded for session {Id}: tier={Tier}, denial={Days}d",
                prev.Id, tier, denialDays);
        }
        else
        {
            // User denies — clear the flag, trust them (for now)
            prev.SuspectedUnauthorizedOrgasm = false;
            await db.SaveChangesAsync(cancellationToken);

            AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme",
                DommeText.Pick(DommeText.ConfessionDenied)));
            _logger.LogInformation("Confession denied for session {Id} — flag cleared", prev.Id);
        }

        RaiseStateChanged();
    }

    public async Task<IReadOnlyList<PunishmentSummary>> GetActivePunishmentSummariesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var active = await db.Punishments
            .Where(p => !p.Completed)
            .Where(p => p.DenialEndsAtUtc == null || p.DenialEndsAtUtc > DateTime.UtcNow)
            .OrderByDescending(p => p.IssuedAtUtc)
            .Take(10)
            .ToListAsync(cancellationToken);

        return active.Select(p => new PunishmentSummary
        {
            Id = p.Id,
            Description = p.Description,
            Category = p.Category,
            Tier = p.Tier,
            RemainingCount = p.RemainingCount,
            DenialEndsAtUtc = p.DenialEndsAtUtc
        }).ToList();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SeedInitialState();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return Task.CompletedTask;
        }

        _cts.Cancel();

        _cts.Dispose();
        _cts = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _onlineEngine.StepTimerElapsed -= OnStepTimerElapsed;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private void SeedInitialState()
    {
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "System", "AI Domme is ready. Start a session, or browse Cove with the tracker."));
    }

    public void RestoreCompanionMessages(IEnumerable<ChatMessage> messages, IEnumerable<Guid> consumed)
    {
        lock (_gate) { _messages.Clear(); _messages.AddRange(messages.TakeLast(MaxMessages)); }
        _consumedTrackerEvents.Clear();
        foreach (var id in consumed) _consumedTrackerEvents.Add(id);
    }
    public Guid[] ConsumedTrackerEvents() => _consumedTrackerEvents.ToArray();
    public void CompanionNotice(string text) => AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Manager", text));
    public void CompanionDirective()
    {
        var directive = _onlineEngine.GetCurrentState().CurrentDirective;
        if (directive is not null) AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(directive)));
    }

    /// <summary>
    /// Write the session outcome to the DB and clear the anti-cheat suspected flag.
    /// Called whenever a session ends through a recognized path (denial, orgasm, manual end).
    /// </summary>
    private async Task FinalizeSessionOutcomeAsync(int sessionId, SessionOutcome outcome, CancellationToken ct = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null)
        {
            session.Outcome = outcome;
            // Only clear the suspected flag when the session ends normally (not Unknown/abandoned)
            if (outcome != SessionOutcome.Unknown)
                session.SuspectedUnauthorizedOrgasm = false;
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Session {Id} finalized: outcome={Outcome}, suspected={Suspected}",
                sessionId, outcome, session.SuspectedUnauthorizedOrgasm);
        }
    }

    private void AddMessage(ChatMessage message)
    {
        lock (_gate)
        {
            _messages.Add(message);
            if (_messages.Count > MaxMessages)
            {
                var itemsToTrim = _messages.Count - MaxMessages;
                _messages.RemoveRange(0, itemsToTrim);
            }
        }

        RaiseStateChanged();
    }

    private (int Speed, TimeSpan HoldDuration) ComputeEdgeParameters()
    {
        // Look up the current session's Intensity (0–10). Default to 5 if unknown.
        var session = GetLatestSessionSync();
        int intensity = session is not null && session.Intensity >= 0 ? session.Intensity : 5;

        // Scale: higher intensity → faster speed, longer hold
        double t = intensity / 10.0;
        int speed = (int)Math.Round(MinEdgeSpeed + t * (MaxEdgeSpeed - MinEdgeSpeed));

        double holdSeconds = MinHoldDuration.TotalSeconds + t * (MaxHoldDuration.TotalSeconds - MinHoldDuration.TotalSeconds);
        // Add a small random jitter (±20%)
        double jitter = 1.0 + (Random.Shared.NextDouble() - 0.5) * 0.4;
        holdSeconds = Math.Max(MinHoldDuration.TotalSeconds, holdSeconds * jitter);

        return (speed, TimeSpan.FromSeconds(holdSeconds));
    }

    private SessionRecord? GetLatestSessionSync()
    {
        using var dbContext = _dbContextFactory.CreateDbContext();
        if (_onlineEngine.HasActiveSession)
        {
            var active = _onlineEngine.GetCurrentState().SessionId;
            return dbContext.Sessions.FirstOrDefault(x => x.Id == active);
        }
        return dbContext.Sessions
            .OrderByDescending(s => s.LastInteractionUtc)
            .FirstOrDefault();
    }

    private void RaiseStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnStepTimerElapsed(SessionDirective nextDirective)
    {
        AddMessage(new ChatMessage(Guid.NewGuid(), DateTime.UtcNow, "Domme", FormatDirectiveForChat(nextDirective)));
        _logger.LogInformation("Step timer auto-advanced. Next: {Text}", nextDirective.Text);
    }

    private string FormatDirectiveForChat(SessionDirective directive)
    {
        var state = _onlineEngine.GetCurrentState();
        if (state.ActiveGameType == GameType.MemorySequence && !state.ActiveGameAnswerReady)
            return "Memorize the sequence on screen now. It will disappear shortly.";

        return _onlineEngine.GetCurrentState().CurrentDirective?.Text ?? directive.Text;
    }
}
