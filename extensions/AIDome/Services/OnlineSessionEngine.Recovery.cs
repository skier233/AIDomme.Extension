using System.Text.Json;
using AIDome2025.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDome2025.Services;

public sealed partial class OnlineSessionEngine
{
    private sealed class EngineCheckpoint
    {
        public EngineCheckpoint() { }
        public int Format { get; set; } = 1;
        public SessionPolicy Policy { get; set; } = new();
        public int Elapsed { get; set; }
        public int Remaining { get; set; }
        public bool TimerArmed { get; set; }
        public int SessionId { get; set; }
        public bool Ended { get; set; }
        public SessionDirective? CurrentDirective { get; set; }
        public int StepNumber { get; set; }
        public int EdgesThisSession { get; set; }
        public int EdgesThisStep { get; set; }
        public int StimBudget { get; set; }
        public int[] RecentMethodIds { get; set; } = [];
        public StimMethod[] EligibleMethods { get; set; } = [];
        public int StepDurationSeconds { get; set; }
        public double Arousal { get; set; }
        public int StepsSinceRest { get; set; }
        public bool Retaliating { get; set; }
        public int RetaliationStepsLeft { get; set; }
        public RetaliationReason RetaliationReason { get; set; }
        public int[] RecentIntensities { get; set; } = [];
        public bool FinishOffered { get; set; }
        public bool PunishmentMode { get; set; }
        public bool LubeActive { get; set; }
        public bool HoldingAtEdge { get; set; }
        public bool GamesOnlyMode { get; set; }
        public int? ActiveGameChallengeId { get; set; }
        public GameType? ActiveGameType { get; set; }
        public int ActiveGameRange { get; set; }
        public bool ActiveGameAnswerReady { get; set; }
        public GameType[] RecentGameTypes { get; set; } = [];
        public int GamePhase { get; set; }
        public int GamePhaseData { get; set; }
        public int GamePhaseData2 { get; set; }
        public bool PendingGameConsequence { get; set; }
        public bool PendingGameConsequenceWon { get; set; }
        public GameType PendingGameConsequenceType { get; set; }
        public SessionDirective? PendingLubeDirective { get; set; }
        public bool LubeWasActive { get; set; }
        public SessionDirective? SuspendedDirective { get; set; }
        public int SuspendedRemainingSeconds { get; set; }
        public bool FalsePromiseActive { get; set; }
        public int FalsePromiseStepsLeft { get; set; }
        public int SessionPoints { get; set; }
        public DirectiveCategory? PreviousCategory { get; set; }
        public StimMethod? ChainMethod { get; set; }
        public int ChainStepsRemaining { get; set; }
        public DommeMood Mood { get; set; }
        public int MoodStepsRemaining { get; set; }
        public double MoodIntensity { get; set; }
        public SessionOutcome SessionOutcome { get; set; }
    }
    public string ExportRecovery()
    {
        lock (_lock)
        {
            var state = GetCurrentState();
            var saved = new EngineCheckpoint
            {
                Policy = Policy, Elapsed = state.ElapsedSeconds, Remaining = state.RemainingSeconds,
                TimerArmed = _paused ? _pausedTimer : _stepTimer is not null,
                SessionId = _sessionId,
                Ended = _ended,
                CurrentDirective = _currentDirective,
                StepNumber = _stepNumber,
                EdgesThisSession = _edgesThisSession,
                EdgesThisStep = _edgesThisStep,
                StimBudget = _stimBudget,
                RecentMethodIds = _recentMethodIds.ToArray(),
                EligibleMethods = _eligibleMethods.ToArray(),
                StepDurationSeconds = _stepDurationSeconds,
                Arousal = _arousal,
                StepsSinceRest = _stepsSinceRest,
                Retaliating = _retaliating,
                RetaliationStepsLeft = _retaliationStepsLeft,
                RetaliationReason = _retaliationReason,
                RecentIntensities = _recentIntensities.ToArray(),
                FinishOffered = _finishOffered,
                PunishmentMode = _punishmentMode,
                LubeActive = _lubeActive,
                HoldingAtEdge = _holdingAtEdge,
                GamesOnlyMode = _gamesOnlyMode,
                ActiveGameChallengeId = _activeGameChallengeId,
                ActiveGameType = _activeGameType,
                ActiveGameRange = _activeGameRange,
                ActiveGameAnswerReady = _activeGameAnswerReady,
                RecentGameTypes = _recentGameTypes.ToArray(),
                GamePhase = _gamePhase,
                GamePhaseData = _gamePhaseData,
                GamePhaseData2 = _gamePhaseData2,
                PendingGameConsequence = _pendingGameConsequence,
                PendingGameConsequenceWon = _pendingGameConsequenceWon,
                PendingGameConsequenceType = _pendingGameConsequenceType,
                PendingLubeDirective = _pendingLubeDirective,
                LubeWasActive = _lubeWasActive,
                SuspendedDirective = _suspendedDirective,
                SuspendedRemainingSeconds = _suspendedRemainingSeconds,
                FalsePromiseActive = _falsePromiseActive,
                FalsePromiseStepsLeft = _falsePromiseStepsLeft,
                SessionPoints = _sessionPoints,
                PreviousCategory = _previousCategory,
                ChainMethod = _chainMethod,
                ChainStepsRemaining = _chainStepsRemaining,
                Mood = _mood,
                MoodStepsRemaining = _moodStepsRemaining,
                MoodIntensity = _moodIntensity,
                SessionOutcome = _sessionOutcome,
            };
            return JsonSerializer.Serialize(saved, AIDome.Persistence.GuidedStore.Json);
        }
    }
    public async Task RestoreRecoveryAsync(string json, CancellationToken ct)
    {
        var saved = JsonSerializer.Deserialize<EngineCheckpoint>(json, AIDome.Persistence.GuidedStore.Json)
            ?? throw new InvalidOperationException("Invalid saved session.");
        if (saved.Format != 1) throw new InvalidOperationException("Unsupported saved engine version.");
        saved.Policy.Validate();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = saved.SessionId > 0 ? await db.Sessions.SingleOrDefaultAsync(x => x.Id == saved.SessionId, ct) : null;
        if (!saved.Ended && saved.SessionId > 0 && session is null) throw new InvalidOperationException("The saved session no longer exists.");
        lock (_lock)
        {
            CancelStepTimer();
            _sessionId = saved.SessionId;
            _ended = saved.Ended;
            _currentDirective = saved.CurrentDirective;
            _stepNumber = saved.StepNumber;
            _edgesThisSession = saved.EdgesThisSession;
            _edgesThisStep = saved.EdgesThisStep;
            _stimBudget = saved.StimBudget;
            _recentMethodIds.Clear(); _recentMethodIds.AddRange(saved.RecentMethodIds);
            _eligibleMethods.Clear(); _eligibleMethods.AddRange(saved.EligibleMethods);
            _stepDurationSeconds = saved.StepDurationSeconds;
            _arousal = saved.Arousal;
            _stepsSinceRest = saved.StepsSinceRest;
            _retaliating = saved.Retaliating;
            _retaliationStepsLeft = saved.RetaliationStepsLeft;
            _retaliationReason = saved.RetaliationReason;
            _recentIntensities.Clear(); foreach (var value in saved.RecentIntensities) _recentIntensities.Enqueue(value);
            _finishOffered = saved.FinishOffered;
            _punishmentMode = saved.PunishmentMode;
            _lubeActive = saved.LubeActive;
            _holdingAtEdge = saved.HoldingAtEdge;
            _gamesOnlyMode = saved.GamesOnlyMode;
            _activeGameChallengeId = saved.ActiveGameChallengeId;
            _activeGameType = saved.ActiveGameType;
            _activeGameRange = saved.ActiveGameRange;
            _activeGameAnswerReady = saved.ActiveGameAnswerReady;
            _recentGameTypes.Clear(); foreach (var value in saved.RecentGameTypes) _recentGameTypes.Enqueue(value);
            _gamePhase = saved.GamePhase;
            _gamePhaseData = saved.GamePhaseData;
            _gamePhaseData2 = saved.GamePhaseData2;
            _pendingGameConsequence = saved.PendingGameConsequence;
            _pendingGameConsequenceWon = saved.PendingGameConsequenceWon;
            _pendingGameConsequenceType = saved.PendingGameConsequenceType;
            _pendingLubeDirective = saved.PendingLubeDirective;
            _lubeWasActive = saved.LubeWasActive;
            _suspendedDirective = saved.SuspendedDirective;
            _suspendedRemainingSeconds = saved.SuspendedRemainingSeconds;
            _falsePromiseActive = saved.FalsePromiseActive;
            _falsePromiseStepsLeft = saved.FalsePromiseStepsLeft;
            _sessionPoints = saved.SessionPoints;
            _previousCategory = saved.PreviousCategory;
            _chainMethod = saved.ChainMethod;
            _chainStepsRemaining = saved.ChainStepsRemaining;
            _mood = saved.Mood;
            _moodStepsRemaining = saved.MoodStepsRemaining;
            _moodIntensity = saved.MoodIntensity;
            _sessionOutcome = saved.SessionOutcome;
            Policy = saved.Policy;
            _session = session;
            _sessionStartUtc = DateTime.UtcNow.AddSeconds(-saved.Elapsed);
            _stepStartUtc = DateTime.UtcNow;
            _stepDurationSeconds = saved.TimerArmed ? Math.Max(1, saved.Remaining) : 0;
            _paused = HasActiveSession;
            _pausedAtUtc = DateTime.UtcNow;
            _pausedTimer = saved.TimerArmed;
            // Timers are deliberately never armed until the user explicitly resumes.
        }
    }
}