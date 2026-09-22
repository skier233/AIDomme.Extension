using AIDome2025.Data;
using AIDome2025.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome2025.Services;

public sealed partial class OnlineSessionEngine : IOnlineSessionEngine
{
    // Length parameter (1-10) → stimulation point budget for the session
    private static readonly int[] LengthToStimBudget = [15, 25, 40, 55, 75, 100, 130, 165, 210, 270];

    private readonly IDbContextFactory<CommandCenterDbContext> _dbFactory;
    private readonly ILogger<OnlineSessionEngine> _logger;
    private readonly IGameEngine _gameEngine;
    private readonly object _lock = new();

    // ── Mutable session state (guarded by _lock) ──
    private int _sessionId;
    private SessionRecord? _session;
    private bool _ended;
    private SessionDirective? _currentDirective;
    private int _stepNumber;
    private int _edgesThisSession;
    private int _edgesThisStep;
    private DateTime _sessionStartUtc;
    private int _stimBudget;
    private readonly List<int> _recentMethodIds = new();
    private List<StimMethod> _eligibleMethods = new();

    // ── Step timer (auto-advance timed directives) ──
    private Timer? _stepTimer;
    private DateTime _stepStartUtc;
    private int _stepDurationSeconds;

    // ── Dynamic factors ──
    private double _arousal;              // 0-100, estimated arousal level
    private int _stepsSinceRest;          // steps since last Rest directive
    private bool _retaliating;            // domme is retaliating after beg-denied etc.
    private int _retaliationStepsLeft;    // how many harsh steps remain
    private RetaliationReason _retaliationReason = RetaliationReason.BegCumDenied;
    private readonly Queue<int> _recentIntensities = new(); // last 5 step intensities
    private bool _finishOffered;          // has the finish directive been shown?
    private bool _punishmentMode;         // harsher directive generation
    private bool _lubeActive;             // lube has been used, dry methods impractical
    private bool _holdingAtEdge;          // currently in post-edge hold countdown
    private bool _gamesOnlyMode;          // debug mode: only generate games, never auto-finish

    // ── Active game state ──
    private int? _activeGameChallengeId;
    private GameType? _activeGameType;
    private int _activeGameRange;
    private bool _activeGameAnswerReady;
    private readonly Queue<GameType> _recentGameTypes = new();
    private int _gamePhase;              // sub-phase index for multi-phase games (0 = initial)
    private int _gamePhaseData;          // generic int: round count, reroll count, etc.
    private int _gamePhaseData2;         // second generic int: total rounds for RLGL, etc.

    // ── Game-only consequence tracking ──
    private bool _pendingGameConsequence;
    private bool _pendingGameConsequenceWon;
    private GameType _pendingGameConsequenceType;

    // ── Lube transition ──
    private SessionDirective? _pendingLubeDirective;  // directive waiting for lube application
    private bool _lubeWasActive;          // lube state at start of step (to detect transitions)

    // ── Suspended timed step (for edge-during-timed resume) ──
    private SessionDirective? _suspendedDirective;    // original timed step to resume after hold/cooldown
    private int _suspendedRemainingSeconds;           // seconds left on the original step

    // ── Psychological state ──
    private bool _falsePromiseActive;     // building anticipation for a false promise
    private int _falsePromiseStepsLeft;   // steps until the "just kidding"
    private int _sessionPoints;           // accumulated points this session

    // ── Flow chaining ──
    private DirectiveCategory? _previousCategory;  // last category for transition biases
    private StimMethod? _chainMethod;              // method being chained across steps
    private int _chainStepsRemaining;              // steps left in current method chain

    // ── Domme mood ──
    private DommeMood _mood;                       // current mood — persists across many steps
    private int _moodStepsRemaining;               // steps until mood may drift
    private double _moodIntensity;                 // 0.0-1.0 how strongly the mood is felt

    // ── Session outcome tracking ──
    private SessionOutcome _sessionOutcome;        // how the session ended

    public OnlineSessionEngine(
        IDbContextFactory<CommandCenterDbContext> dbFactory,
        ILogger<OnlineSessionEngine> logger,
        IGameEngine gameEngine)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _gameEngine = gameEngine;
    }

    public bool HasActiveSession
    {
        get { lock (_lock) { return !_ended && _sessionId > 0; } }
    }

    public event Action<SessionDirective>? StepTimerElapsed;

    public OnlineSessionState GetCurrentState()
    {
        lock (_lock)
        {
            int elapsed = _sessionId > 0 && !_ended
                ? (int)((_paused ? _pausedAtUtc : DateTime.UtcNow) - _sessionStartUtc).TotalSeconds
                : 0;

            int remaining = -1;
            if (_currentDirective is not null && _stepDurationSeconds > 0)
            {
                int stepElapsed = (int)((_paused ? _pausedAtUtc : DateTime.UtcNow) - _stepStartUtc).TotalSeconds;
                remaining = Math.Max(0, _stepDurationSeconds - stepElapsed);
            }

            return new OnlineSessionState
            {
                IsPaused = _paused,
                SessionId = _sessionId,
                Phase = DerivePhase(),
                CurrentDirective = _currentDirective,
                StepNumber = _stepNumber,
                EdgesThisSession = _edgesThisSession,
                ElapsedSeconds = elapsed,
                TotalBudgetSeconds = _stimBudget,
                Arousal = (int)Math.Round(_arousal),
                IsFinished = _ended,
                RemainingSeconds = remaining,
                Points = _sessionPoints,
                ActiveGameChallengeId = _activeGameChallengeId,
                ActiveGameType = _activeGameType,
                ActiveGameRange = _activeGameRange,
                ActiveGameAnswerReady = _activeGameAnswerReady,
                Mood = _mood.ToString(),
                IsLubePause = _pendingLubeDirective is not null,
                Outcome = _sessionOutcome
            };
        }
    }

    public Task<SessionDirective> BeginSessionAsync(int sessionId, bool onlyGamesMode = false, CancellationToken ct = default)
        => BeginSessionCoreAsync(sessionId, punishment: false, onlyGamesMode, ct);

    public Task<SessionDirective> BeginPunishmentSessionAsync(int sessionId, bool onlyGamesMode = false, CancellationToken ct = default)
        => BeginSessionCoreAsync(sessionId, punishment: true, onlyGamesMode, ct);

    private async Task<SessionDirective> BeginSessionCoreAsync(int sessionId, bool punishment, bool onlyGamesMode, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        var allMethods = await db.StimMethods.ToListAsync(ct);
        var eligible = PolicyMethods(FilterMethodsForSession(allMethods, session));
        if (Policy.Guidance != "Permission" && eligible.Count == 0) throw new ArgumentException("No available methods match the accepted settings.");

        lock (_lock)
        {
            CancelStepTimer();
            _sessionId = sessionId;
            _session = session;
            _ended = false;
            _paused = false;
            _punishmentMode = punishment;
            _lubeActive = false;
            _holdingAtEdge = false;
            _gamesOnlyMode = onlyGamesMode;
            _stepNumber = 0;
            _edgesThisSession = 0;
            _edgesThisStep = 0;
            _sessionStartUtc = DateTime.UtcNow;
            _recentMethodIds.Clear();
            _eligibleMethods = eligible;
            _arousal = 0;
            _stepsSinceRest = 0;
            _retaliating = false;
            _retaliationStepsLeft = 0;
            _retaliationReason = RetaliationReason.BegCumDenied;
            _recentIntensities.Clear();
            _finishOffered = false;
            _falsePromiseActive = false;
            _falsePromiseStepsLeft = 0;
            _sessionPoints = 0;
            _pendingLubeDirective = null;
            _suspendedDirective = null;
            _suspendedRemainingSeconds = 0;
            _previousCategory = null;
            _chainMethod = null;
            _chainStepsRemaining = 0;
            _mood = PickInitialMood(punishment);
            _moodStepsRemaining = 4 + Random.Shared.Next(5); // 4-8 steps
            _moodIntensity = 0.4 + Random.Shared.NextDouble() * 0.4; // 0.4-0.8
            _activeGameChallengeId = null;
            _activeGameType = null;
            _activeGameRange = 0;
            _activeGameAnswerReady = false;
            _gamePhase = 0;
            _gamePhaseData = 0;
            _gamePhaseData2 = 0;
            _recentGameTypes.Clear();
            _pendingGameConsequence = false;
            _pendingGameConsequenceWon = false;
            _pendingGameConsequenceType = GameType.CoinFlip;
            _sessionOutcome = SessionOutcome.Unknown;

            int length = Math.Clamp(session.Length > 0 ? session.Length : 5, 1, 10);
            _stimBudget = LengthToStimBudget[length - 1];
        }

        _logger.LogInformation(
            "Online session {Id} started (punishment={Punishment}). StimBudget={Budget}, Methods={Count}",
            sessionId, punishment, _stimBudget, eligible.Count);

        return NextDirective();
    }

    public Task<SessionDirective> CompleteStepAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_ended || _sessionId == 0)
                throw new InvalidOperationException("No active session");
            if (_paused) throw new InvalidOperationException("Resume the session first.");

            CancelStepTimer();

            // If the finish directive was the one just completed → session ends as denial
            if (_currentDirective?.Kind == InstructionKind.Finish)
            {
                _ended = true;
                _sessionOutcome = SessionOutcome.Denied;
                _currentDirective = null;
                _logger.LogInformation("Session {Id} ended (denial accepted)", _sessionId);
                return Task.FromResult(EndDirective(wasOrgasmGranted: false));
            }

            // If we just completed the cum-permission timed step → session ends (orgasm)
            if (_currentDirective?.Phase == SessionPhase.Finishing)
            {
                _ended = true;
                _sessionOutcome = SessionOutcome.AllowedOrgasm;
                _currentDirective = null;
                _logger.LogInformation("Session {Id} ended (orgasm completed)", _sessionId);
                return Task.FromResult(EndDirective(wasOrgasmGranted: true));
            }

            UpdateArousalForCompletedStep();
            _edgesThisStep = 0;
        }

        return Task.FromResult(NextDirective());
    }

    public Task<SessionDirective?> RecordEdgeAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_ended || _sessionId == 0)
                return Task.FromResult<SessionDirective?>(null);
            if (_paused) return Task.FromResult<SessionDirective?>(null);

            _edgesThisSession++;
            _edgesThisStep++;
            if (Policy.Guidance == "Permission") return Task.FromResult<SessionDirective?>(null);
            _arousal = Math.Min(95, _arousal + 15);

            if (_activeGameType == GameType.EdgeCounter && _activeGameChallengeId is not null)
            {
                if (_activeGameAnswerReady)
                    return Task.FromResult<SessionDirective?>(null);

                if (_edgesThisStep >= _activeGameRange)
                {
                    CancelStepTimer();
                    _activeGameAnswerReady = true;
                    var countDirective = new SessionDirective
                    {
                        Kind = InstructionKind.FreeText,
                        DurationOrCount = 0,
                        Text = "Stop. Now tell me how many edges that was.",
                        Phase = SessionPhase.Edging
                    };
                    _currentDirective = PolicyDirective(countDirective);
                    LogDirective(countDirective);
                    return Task.FromResult<SessionDirective?>(countDirective);
                }

                return Task.FromResult<SessionDirective?>(null);
            }

            // ── ArousalChallenge: edging fails immediately, or in a cruel mood forces the remainder ──
            if (_activeGameType == GameType.ArousalChallenge && _activeGameChallengeId is not null)
            {
                bool forceFinishTimer = _mood is DommeMood.Cruel or DommeMood.Relentless
                    || (Param(s => s.Mean, 5) >= 7 && Random.Shared.Next(100) < 40);

                if (!forceFinishTimer)
                {
                    CancelStepTimer();
                    AutoResolveGameLocked(answer: 1, won: false);
                    _logger.LogInformation("Session {Id}: ArousalChallenge — edged, immediate auto-loss", _sessionId);
                    var lossDirective = NextDirectiveLocked();
                    return Task.FromResult<SessionDirective?>(lossDirective);
                }

                int stepElapsed = (int)(DateTime.UtcNow - _stepStartUtc).TotalSeconds;
                int remaining = Math.Max(0, _stepDurationSeconds - stepElapsed);
                if (remaining <= 0)
                {
                    CancelStepTimer();
                    AutoResolveGameLocked(answer: 1, won: false);
                    var expiredLoss = NextDirectiveLocked();
                    return Task.FromResult<SessionDirective?>(expiredLoss);
                }

                CancelStepTimer();
                _gamePhase = 1; // already failed, but must finish the remaining timer
                _stepStartUtc = DateTime.UtcNow;
                _stepDurationSeconds = remaining;
                ArmTimer(remaining);

                var continueDirective = new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = remaining,
                    Text = $"You already failed. Keep going for the remaining {remaining} seconds anyway.",
                    Phase = SessionPhase.Tormenting
                };
                _currentDirective = PolicyDirective(continueDirective);
                LogDirective(continueDirective);
                return Task.FromResult<SessionDirective?>(continueDirective);
            }

            // ── RaceToEdge: hitting the edge in time is an immediate win ──
            if (_activeGameType == GameType.RaceToEdge && _activeGameChallengeId is not null)
            {
                CancelStepTimer();
                AutoResolveGameLocked(answer: 1, won: true);
                _logger.LogInformation("Session {Id}: RaceToEdge — reached the edge in time", _sessionId);
                var winDirective = NextDirectiveLocked();
                return Task.FromResult<SessionDirective?>(winDirective);
            }

            // ── EdgeEndurance: first edge starts the hold timer ──
            if (_activeGameType == GameType.EdgeEndurance && _activeGameChallengeId is not null && _gamePhase == 0)
            {
                _gamePhase = 1;
                CancelStepTimer();
                int holdSec = _activeGameRange; // stored from the challenge's CorrectAnswer
                if (holdSec <= 0) holdSec = 15;
                _stepStartUtc = DateTime.UtcNow;
                _stepDurationSeconds = holdSec;
                ArmTimer(holdSec);

                var holdDirective = new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = holdSec,
                    Text = $"You're at the edge. Now HOLD it there for {holdSec} seconds. Don't you dare go over.",
                    Phase = SessionPhase.Edging
                };
                _currentDirective = PolicyDirective(holdDirective);
                LogDirective(holdDirective);
                return Task.FromResult<SessionDirective?>(holdDirective);
            }

            // ── Edge during a timed method: decide hold-or-stop ──
            if (_currentDirective?.Kind == InstructionKind.TimedMethod)
            {
                int mean = Param(s => s.Mean, 5);
                bool shouldHold = Random.Shared.Next(100) < (25 + mean * 5); // Mean=5→50%, Mean=10→75%

                // Calculate how much time remains on the current timed step
                int stepElapsed = (int)(DateTime.UtcNow - _stepStartUtc).TotalSeconds;
                int remaining = Math.Max(0, _stepDurationSeconds - stepElapsed);

                if (shouldHold)
                {
                    // Hold at edge for a few seconds before resuming
                    int holdSec = _punishmentMode
                        ? Random.Shared.Next(5, 15)
                        : Random.Shared.Next(3, 10);

                    // Save the original step so we can resume after hold
                    if (remaining > 5)
                    {
                        _suspendedDirective = _currentDirective;
                        _suspendedRemainingSeconds = remaining;
                    }

                    CancelStepTimer();
                    _holdingAtEdge = true;
                    _stepStartUtc = DateTime.UtcNow;
                    _stepDurationSeconds = holdSec;
                    ArmTimer(holdSec);

                    var holdCtx = BuildSpeechContext(DommeVoice.DirectiveCategory.Edge, _currentDirective.Method, _currentDirective.Speed, holdSec);
                    string holdText = DommeVoice.ComposeEdgeHold(holdCtx, holdSec);
                    var holdDirective = new SessionDirective
                    {
                        Kind = InstructionKind.FreeText,
                        DurationOrCount = holdSec,
                        Text = holdText,
                        Phase = SessionPhase.Edging
                    };
                    _currentDirective = PolicyDirective(holdDirective);
                    LogDirective(holdDirective);
                    return Task.FromResult<SessionDirective?>(holdDirective);
                }
                else
                {
                    // Stop and cool down briefly, then resume original step
                    int cooldown = Random.Shared.Next(3, 8);

                    _sessionPoints += 2; // bonus points for edging during timed

                    // Save the original step so we can resume after cooldown
                    if (remaining > 5)
                    {
                        _suspendedDirective = _currentDirective;
                        _suspendedRemainingSeconds = remaining;
                    }

                    CancelStepTimer();
                    var coolCtx = BuildSpeechContext(DommeVoice.DirectiveCategory.Surprise, durationOrCount: cooldown);
                    string stopText = DommeVoice.ComposeCooldown(coolCtx, cooldown);
                    var coolDirective = new SessionDirective
                    {
                        Kind = InstructionKind.Rest,
                        DurationOrCount = cooldown,
                        Text = stopText,
                        Phase = SessionPhase.Resting
                    };
                    _currentDirective = PolicyDirective(coolDirective);
                    LogDirective(coolDirective);
                    StartStepTimerIfTimed(coolDirective);
                    return Task.FromResult<SessionDirective?>(coolDirective);
                }
            }

            // ── Edge during an EdgeCount step ──
            if (_currentDirective?.Kind == InstructionKind.EdgeCount
                && _edgesThisStep >= _currentDirective.DurationOrCount)
            {
                // Post-edge hold: start a timer before auto-advancing
                if (_currentDirective.HoldSeconds > 0 && !_holdingAtEdge)
                {
                    _holdingAtEdge = true;
                    CancelStepTimer();
                    _stepStartUtc = DateTime.UtcNow;
                    _stepDurationSeconds = _currentDirective.HoldSeconds;
                    ArmTimer(_stepDurationSeconds);

                    var holdCtx = BuildSpeechContext(DommeVoice.DirectiveCategory.Edge,
                        _currentDirective.Method, _currentDirective.Speed, _currentDirective.HoldSeconds);
                    string holdText = DommeVoice.ComposeEdgeCountHold(holdCtx, _currentDirective.HoldSeconds);
                    var holdDirective = new SessionDirective
                    {
                        Kind = InstructionKind.FreeText,
                        DurationOrCount = _currentDirective.HoldSeconds,
                        Text = holdText,
                        Phase = SessionPhase.Edging
                    };
                    _currentDirective = PolicyDirective(holdDirective);
                    LogDirective(holdDirective);
                    return Task.FromResult<SessionDirective?>(holdDirective);
                }

                _holdingAtEdge = false;
                UpdateArousalForCompletedStep();
                _edgesThisStep = 0;
                return Task.FromResult<SessionDirective?>(NextDirectiveLocked());
            }
        }

        return Task.FromResult<SessionDirective?>(null);
    }

    public void EndSession()
    {
        EndSession(SessionOutcome.Unknown);
    }

    public void EndSession(SessionOutcome outcome)
    {
        lock (_lock)
        {
            CancelStepTimer();
            _ended = true;
            _currentDirective = null;
            _activeGameChallengeId = null;
            _activeGameType = null;
            _activeGameRange = 0;
            _activeGameAnswerReady = false;
            _gamePhase = 0;
            _gamePhaseData = 0;
            _gamePhaseData2 = 0;
            _sessionOutcome = outcome;
            _logger.LogInformation("Online session {Id} ended (outcome={Outcome})", _sessionId, outcome);
        }
    }

    public SessionDirective ResolveGame(bool won)
    {
        lock (_lock)
        {
            if (_ended || _sessionId == 0)
                throw new InvalidOperationException("No active session");

            var lastType = _activeGameType ?? GameType.CoinFlip;
            CancelStepTimer();
            _activeGameChallengeId = null;
            _activeGameType = null;
            _activeGameRange = 0;
            _activeGameAnswerReady = false;
            _gamePhase = 0;
            _gamePhaseData = 0;
            _gamePhaseData2 = 0;
            _edgesThisStep = 0;

            if (won)
            {
                _sessionPoints += 5;
                NudgeMood(-0.4); // pleased — soften
                _logger.LogInformation("Session {Id}: game won, +5 points", _sessionId);
            }
            else
            {
                _retaliating = true;
                _retaliationStepsLeft = 2 + Random.Shared.Next(3);
                NudgeMood(0.6); // angry — harshen
                _logger.LogInformation("Session {Id}: game lost, retaliation ON ({Steps} steps)",
                    _sessionId, _retaliationStepsLeft);
            }

            if (_gamesOnlyMode)
            {
                _pendingGameConsequence = true;
                _pendingGameConsequenceWon = won;
                _pendingGameConsequenceType = lastType;
                return NextDirectiveLocked();
            }

            return NextDirectiveLocked();
        }
    }

    public SessionDirective? HandleReroll(int newOutcome)
    {
        lock (_lock)
        {
            if (_ended || _sessionId == 0)
                throw new InvalidOperationException("No active session");

            if (_activeGameType != GameType.RerollGamble || _activeGameChallengeId is null)
                return null;

            _gamePhaseData++;
            int worseningBias = Math.Max(0, _gamePhaseData - 1);
            int worsenedOutcome = Math.Max(1, newOutcome - worseningBias);

            if (_activeGameChallengeId is int challengeId)
            {
                try
                {
                    using var db = _dbFactory.CreateDbContext();
                    var challenge = db.GameChallenges.FirstOrDefault(c => c.Id == challengeId);
                    if (challenge is not null)
                    {
                        challenge.CorrectAnswer = worsenedOutcome;
                        challenge.SuccessReward = GameEngine.DescribeRerollOutcome(worsenedOutcome);
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist reroll outcome for challenge {Id}", challengeId);
                }
            }

            if (_gamePhaseData >= _gamePhaseData2)
            {
                // Max rerolls reached — force accept
                _activeGameRange = 0;
                var forcedDirective = new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0,
                    Text = $"No more rerolls. The house wins. Your final fate is: {GameEngine.DescribeRerollOutcome(worsenedOutcome)}. Accept it.",
                    Phase = SessionPhase.Intense
                };
                _currentDirective = PolicyDirective(forcedDirective);
                LogDirective(forcedDirective);
                return forcedDirective;
            }

            string newOutcomeText = GameEngine.DescribeRerollOutcome(worsenedOutcome);
            string prompt = $"Reroll #{_gamePhaseData}. New fate: {newOutcomeText} Still want to push your luck?";
            var directive = new SessionDirective
            {
                Kind = InstructionKind.FreeText,
                DurationOrCount = 0,
                Text = prompt + " Enter 0 to accept, or 1 to gamble again.",
                Phase = SessionPhase.Intense
            };
            _currentDirective = PolicyDirective(directive);
            LogDirective(directive);
            return directive;
        }
    }

    /// <summary>
    /// Auto-resolve the current game from within the engine (e.g. on timer expiry).
    /// Submits the answer to the DB synchronously and updates internal state.
    /// Caller MUST hold _lock.
    /// </summary>
    private void AutoResolveGameLocked(int answer, bool won)
    {
        if (_activeGameChallengeId is int challengeId)
        {
            try
            {
                _gameEngine.SubmitAnswerAsync(challengeId, answer).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to auto-resolve game {Id}", challengeId);
            }
        }

        var lastType = _activeGameType ?? GameType.CoinFlip;
        _activeGameChallengeId = null;
        _activeGameType = null;
        _activeGameRange = 0;
        _activeGameAnswerReady = false;
        _gamePhase = 0;
        _gamePhaseData = 0;
        _gamePhaseData2 = 0;
        _edgesThisStep = 0;

        if (won)
        {
            _sessionPoints += 5;
            NudgeMood(-0.4);
        }
        else
        {
            _retaliating = true;
            _retaliationStepsLeft = 2 + Random.Shared.Next(3);
            NudgeMood(0.6);
        }

        if (_gamesOnlyMode)
        {
            _pendingGameConsequence = true;
            _pendingGameConsequenceWon = won;
            _pendingGameConsequenceType = lastType;
        }
    }

    public void NotifyEvent(SessionEvent evt)
    {
        lock (_lock)
        {
            switch (evt)
            {
                case SessionEvent.BegDenied:
                    _retaliating = true;
                    _retaliationReason = RetaliationReason.BegCumDenied;
                    _retaliationStepsLeft = 2 + Random.Shared.Next(3); // 2-4 harsh steps
                    NudgeMood(0.5); // annoyed by begging
                    _logger.LogInformation("Session {Id}: retaliation mode ON ({Steps} steps)",
                        _sessionId, _retaliationStepsLeft);
                    break;

                case SessionEvent.BegGranted:
                    _ended = true;
                    _currentDirective = null;
                    break;

                case SessionEvent.BegStopDenied:
                    _retaliating = true;
                    _retaliationReason = RetaliationReason.BegStopDenied;
                    _retaliationStepsLeft = 2 + Random.Shared.Next(3);
                    NudgeMood(0.7); // very annoyed by wanting to stop
                    _logger.LogInformation("Session {Id}: stop-retaliation mode ON ({Steps} steps)",
                        _sessionId, _retaliationStepsLeft);
                    break;

                case SessionEvent.BegStopGranted:
                    // Granted a rest — don't end session, just reset current step
                    NudgeMood(-0.2); // mild sympathy
                    CancelStepTimer();
                    break;

                case SessionEvent.InfractionReported:
                    _retaliating = true;
                    _retaliationReason = RetaliationReason.Disobedience;
                    _retaliationStepsLeft = 3 + Random.Shared.Next(4);
                    NudgeMood(0.8); // furious
                    _logger.LogInformation("Session {Id}: infraction retaliation ON ({Steps} steps)",
                        _sessionId, _retaliationStepsLeft);
                    break;

                case SessionEvent.OrgasmReported:
                    _retaliating = true;
                    _retaliationReason = RetaliationReason.UnauthorizedOrgasm;
                    _retaliationStepsLeft = 4 + Random.Shared.Next(4); // 4-7 harsh steps
                    NudgeMood(1.0); // maximum fury
                    _logger.LogInformation("Session {Id}: unauthorized orgasm retaliation ON ({Steps} steps)",
                        _sessionId, _retaliationStepsLeft);
                    break;

                case SessionEvent.RuinedOrgasmReported:
                    _retaliating = true;
                    _retaliationReason = RetaliationReason.RuinedOrgasm;
                    _retaliationStepsLeft = 3 + Random.Shared.Next(3); // 3-5 harsh steps
                    NudgeMood(0.7);
                    _logger.LogInformation("Session {Id}: ruined orgasm retaliation ON ({Steps} steps)",
                        _sessionId, _retaliationStepsLeft);
                    break;
            }
        }
    }

    // ──────────────────── Core decision engine ────────────────────

    private SessionDirective NextDirective()
    {
        lock (_lock)
        {
            return NextDirectiveLocked();
        }
    }

    /// <summary>
    /// Core logic for generating the next directive. Caller MUST hold _lock.
    /// </summary>
    private SessionDirective NextDirectiveLocked()
    {
        _stepNumber++;

        // Cancel any previous step timer
        CancelStepTimer();
        if (Policy.Guidance == "Permission")
        {
            _currentDirective = new SessionDirective { Kind = InstructionKind.FreeText, Text = "Browse freely. Ask me when you want permission to orgasm.", Phase = SessionPhase.Teasing };
            return _currentDirective;
        }

        // If there's a pending directive from a lube pause, deliver it now
        if (_pendingLubeDirective is not null)
        {
            var pending = _pendingLubeDirective;
            _pendingLubeDirective = null;
            _currentDirective = PolicyDirective(pending);
            LogDirective(pending);
            StartStepTimerIfTimed(pending);
            return pending;
        }

        // Small natural arousal decay between steps
        _arousal = Math.Max(0, _arousal - 2);

        // Drift the domme's mood over time
        MaybeDriftMood();

        // Award stimulation points for the just-completed step
        AwardStimPoints();

        if (_pendingGameConsequence)
        {
            _pendingGameConsequence = false;
            var consequence = BuildGameOnlyConsequence(_pendingGameConsequenceWon, _pendingGameConsequenceType);
            _currentDirective = PolicyDirective(consequence);
            LogDirective(consequence);
            StartStepTimerIfTimed(consequence);
            return consequence;
        }

        // Check if stimulation point budget is met → auto-decide finish
        if (Policy.Permission == "EngineFinish" && !_gamesOnlyMode && _activeGameChallengeId is null && _sessionPoints >= _stimBudget && !_finishOffered && !_retaliating)
        {
            _finishOffered = true;
            // Auto-decide denial/permission — the user doesn't get a say at session end
            var finishDirective = BuildAutoFinish();
            _currentDirective = PolicyDirective(finishDirective);
            LogDirective(finishDirective);
            if (finishDirective.Kind == InstructionKind.Finish)
            {
                // Denial — end immediately after showing the text
                _stepDurationSeconds = 0;
            }
            else
            {
                // Permission granted — give them time to cum
                StartStepTimerIfTimed(finishDirective);
            }
            return finishDirective;
        }

        // Tick down retaliation
        if (_retaliating && _retaliationStepsLeft > 0)
            _retaliationStepsLeft--;
        if (_retaliating && _retaliationStepsLeft <= 0)
            _retaliating = false;

        // False promise reveal check
        if (_falsePromiseActive)
        {
            _falsePromiseStepsLeft--;
            if (_falsePromiseStepsLeft <= 0)
            {
                _falsePromiseActive = false;
                var revealDirective = BuildFalsePromiseReveal();
                _currentDirective = PolicyDirective(revealDirective);
                LogDirective(revealDirective);
                StartStepTimerIfTimed(revealDirective);
                return revealDirective;
            }
        }

        // Capture lube state before building the directive
        _lubeWasActive = _lubeActive;

        // Pick a category using weighted factors
        var category = ChooseCategory();
        var directive = category switch
        {
            DirectiveCategory.Tease => BuildTease(),
            DirectiveCategory.Stroke => BuildStroke(),
            DirectiveCategory.Edge => BuildEdge(),
            DirectiveCategory.Rest => BuildRest(),
            DirectiveCategory.Torment => BuildTorment(),
            DirectiveCategory.Surprise => BuildSurprise(),
            DirectiveCategory.Game => BuildGame(),
            DirectiveCategory.Psych => BuildPsych(),
            _ => BuildStroke()
        };

        // Note: false promise buildup text is now handled inside DommeVoice.PickTransitionOpener()
        // via the FalsePromiseActive flag in SpeechContext, so no explicit prepend needed here.

        // Track intensity
        int stepIntensity = directive.Method?.Intensity ?? 0;
        _recentIntensities.Enqueue(stepIntensity);
        if (_recentIntensities.Count > 5) _recentIntensities.Dequeue();

        if (directive.Kind != InstructionKind.Rest)
            _stepsSinceRest++;
        else
            _stepsSinceRest = 0;

        // ── Flow chaining state ──
        _previousCategory = category;
        if (directive.Method is not null
            && directive.Method.Closeness >= 3 && directive.Method.Tease < 6
            && category is DirectiveCategory.Stroke or DirectiveCategory.Edge)
        {
            if (_chainStepsRemaining <= 0 || _chainMethod?.Id != directive.Method.Id)
            {
                _chainMethod = directive.Method;
                _chainStepsRemaining = 1 + Random.Shared.Next(2); // 1-2 more steps with same method
            }
        }
        else if (category is DirectiveCategory.Rest or DirectiveCategory.Torment or DirectiveCategory.Tease)
        {
            _chainStepsRemaining = 0; // break chain after rest, torment, or tease
        }

        // Lube transition: if this directive uses a lube method and lube wasn't
        // active before, show a pause step and queue the actual directive
        if (!_lubeWasActive && directive.Method?.RequiresLube == true)
        {
            _pendingLubeDirective = directive;
            var lubeStep = new SessionDirective
            {
                Kind = InstructionKind.FreeText,
                DurationOrCount = 0, // no auto-advance — user presses "Complete Step"
                Text = "Apply lube now. Press Complete Step when you're ready.",
                Phase = SessionPhase.Resting
            };
            _currentDirective = PolicyDirective(lubeStep);
            LogDirective(lubeStep);
            return lubeStep;
        }

        _currentDirective = PolicyDirective(directive);
        LogDirective(directive);

        // Start auto-advance timer for timed directives
        StartStepTimerIfTimed(directive);

        return directive;
    }

    /// <summary>Persistent domme mood that colors category/transition choices over many steps.</summary>
    private enum DommeMood
    {
        Neutral,       // balanced, no strong preference
        Cruel,         // torment + intense edges, back-to-back punishment
        Edgy,          // rapid-fire edging with moderate methods (keep user on the brink)
        Teasing,       // slow-burn tease and denial, frustrating pacing
        Playful,       // games, surprises, psych — unpredictable
        Relentless     // non-stop stroking and edges, barely any rest
    }

    private enum DirectiveCategory { Tease, Stroke, Edge, Rest, Torment, Surprise, Game, Psych }

    private DirectiveCategory ChooseCategory()
    {
        if (_gamesOnlyMode)
            return DirectiveCategory.Game;

        int mean = Param(s => s.Mean, 5);
        int intensity = Param(s => s.Intensity, 5);
        double progress = SessionProgress();
        double recentAvg = _recentIntensities.Count > 0 ? _recentIntensities.Average() : 0;

        // Base weights — punishment mode is much harsher
        double wTease, wStroke, wEdge, wRest, wTorment, wSurprise, wGame, wPsych;
        if (_punishmentMode)
        {
            wTease = 5;
            wStroke = 10;
            wEdge = 35;
            wRest = 5;
            wTorment = 35;
            wSurprise = 10;
            wGame = 5;
            wPsych = 3;
        }
        else
        {
            wTease = 15;
            wStroke = 30;
            wEdge = 20;
            wRest = 15;
            wTorment = 10;
            wSurprise = 10;
            wGame = 8;
            wPsych = 5;
        }

        // ── Arousal adjustments ──
        if (_arousal > 80)
        {
            wRest *= 2.5;
            wEdge *= 0.4;
            wTease *= 2.0;    // teasing at high arousal is cruel
        }
        else if (_arousal > 55)
        {
            wEdge *= 1.8;     // good arousal zone for edging
        }
        else if (_arousal < 25)
        {
            wTease *= 1.5;
            wStroke *= 1.5;
            wEdge *= 0.2;     // not aroused enough
            wRest *= 0.3;     // doesn't need rest
        }

        // ── Mean-level adjustments ──
        double meanFactor = mean / 5.0;       // 0.2–2.0
        wTorment *= meanFactor;
        wRest *= Math.Max(0.2, 2.0 - meanFactor);
        wSurprise *= meanFactor;
        wGame *= Math.Max(0.5, meanFactor);
        wPsych *= meanFactor;

        // ── Rest pressure ──
        if (_stepsSinceRest >= 5)
            wRest *= 2.0;
        if (_stepsSinceRest >= 7)
            wRest *= 2.0;      // cumulative
        // Never allow consecutive rest steps
        if (_currentDirective?.Kind == InstructionKind.Rest)
            wRest = 0;
        // Never allow consecutive surprise stops
        if (_currentDirective?.Kind == InstructionKind.FreeText)
            wSurprise = 0;

        // ── Recent intensity ──
        if (recentAvg > 7 && mean < 6)
        {
            wRest *= 1.5;
            wTease *= 1.5;
        }
        if (mean >= 7 && recentAvg > 6)
        {
            wTorment *= 1.5;   // mean domme pushes harder after hard blocks
        }

        // ── Session progress ──
        if (progress > 0.4)
            wGame *= 1.5;       // games become more likely mid-session
        if (progress > 0.6)
            wPsych *= 2.0;      // psychological elements ramp up
        if (progress > 0.7)
            wEdge *= 2.0;
        if (progress > 0.9)
        {
            wEdge *= 1.5;
            wTorment *= 1.3;
        }

        // ── Retaliation mode ──
        if (_retaliating)
        {
            wTorment *= 4.0;
            wEdge *= 3.0;
            wRest = 1.0;
            wTease *= 0.1;
            wSurprise *= 0.5;
            wGame *= 0.2;       // no games during retaliation
            wPsych *= 0.3;
        }

        // ── Domme mood ──
        // The mood applies a persistent lens over category selection.
        // _moodIntensity scales how strongly the mood is felt (0.1-1.0).
        double mi = _moodIntensity;
        switch (_mood)
        {
            case DommeMood.Cruel:
                wTorment *= 1.0 + 2.5 * mi;   // heavy torment
                wEdge *= 1.0 + 1.5 * mi;      // cruel edging
                wRest *= Math.Max(0.1, 1.0 - 0.6 * mi);   // minimal mercy
                wTease *= Math.Max(0.2, 1.0 - 0.5 * mi);
                wGame *= Math.Max(0.3, 1.0 - 0.3 * mi);
                break;
            case DommeMood.Edgy:
                wEdge *= 1.0 + 3.0 * mi;      // rapid-fire edges
                wStroke *= 1.0 + 1.0 * mi;    // keep building
                wTorment *= Math.Max(0.3, 1.0 - 0.5 * mi); // avoid going too hard
                wTease *= Math.Max(0.3, 1.0 - 0.4 * mi);
                wSurprise *= Math.Max(0.3, 1.0 - 0.3 * mi);
                break;
            case DommeMood.Teasing:
                wTease *= 1.0 + 3.0 * mi;     // heavy teasing
                wStroke *= 1.0 + 0.5 * mi;    // slow stroking
                wEdge *= Math.Max(0.3, 1.0 - 0.5 * mi);   // delay edges
                wTorment *= Math.Max(0.2, 1.0 - 0.6 * mi);
                wPsych *= 1.0 + 1.0 * mi;     // psych pairs well with teasing
                break;
            case DommeMood.Playful:
                wGame *= 1.0 + 3.0 * mi;      // lots of games
                wSurprise *= 1.0 + 2.0 * mi;  // unpredictable
                wPsych *= 1.0 + 1.5 * mi;
                wTorment *= Math.Max(0.3, 1.0 - 0.4 * mi);
                break;
            case DommeMood.Relentless:
                wStroke *= 1.0 + 2.0 * mi;    // non-stop stroking
                wEdge *= 1.0 + 2.0 * mi;      // constant edging
                wRest *= Math.Max(0.05, 1.0 - 0.8 * mi);  // almost no rest
                wTease *= Math.Max(0.2, 1.0 - 0.6 * mi);  // no time for teasing
                wGame *= Math.Max(0.2, 1.0 - 0.5 * mi);
                wSurprise *= Math.Max(0.3, 1.0 - 0.4 * mi);
                break;
            case DommeMood.Neutral:
            default:
                break; // no extra bias
        }

        // ── Flow chaining: transition biases ──
        // Bias the next category based on what just happened, so the session
        // flows naturally instead of jumping between categories randomly.
        if (_previousCategory is not null)
        {
            switch (_previousCategory.Value)
            {
                case DirectiveCategory.Tease:
                    wStroke *= 2.5;   // teasing builds into stroking
                    wTease *= 0.5;    // don't tease forever
                    wSurprise *= 0.3; // don't interrupt the buildup
                    wGame *= 0.3;
                    break;
                case DirectiveCategory.Stroke:
                    wStroke *= 1.8;   // keep stroking (escalate)
                    wEdge *= 2.0;     // natural escalation toward edge
                    wTease *= 0.4;    // don't de-escalate
                    wSurprise *= 0.4;
                    wGame *= 0.3;
                    break;
                case DirectiveCategory.Edge:
                    wRest *= 2.0;     // body needs break after edging
                    wStroke *= 1.5;   // or keep building
                    wEdge *= 1.3;     // more edges ok if aroused
                    wTease *= 0.3;
                    wSurprise *= 0.3;
                    wGame *= 0.4;
                    break;
                case DirectiveCategory.Rest:
                    // Natural break point — interruptions are welcome here
                    wStroke *= 2.0;   // rebuild
                    wTease *= 1.5;    // tease before rebuilding
                    wEdge *= 0.3;     // just rested, don't jump straight to edge
                    wGame *= 2.0;     // games fit at break points
                    wPsych *= 2.0;    // psych fits at break points
                    wSurprise *= 1.5;
                    break;
                case DirectiveCategory.Torment:
                    wRest *= 2.5;     // relief after torment
                    wStroke *= 1.3;
                    wTorment *= 0.4;  // don't chain torment endlessly
                    wSurprise *= 0.3;
                    wGame *= 0.3;
                    break;
                case DirectiveCategory.Surprise:
                    wStroke *= 2.0;   // resume normal flow
                    wEdge *= 1.5;
                    wSurprise *= 0.1; // don't chain surprises
                    wGame *= 0.3;
                    break;
                case DirectiveCategory.Game:
                    wStroke *= 2.0;   // resume building
                    wEdge *= 1.3;
                    wGame *= 0.1;     // don't chain games
                    wSurprise *= 0.3;
                    break;
                case DirectiveCategory.Psych:
                    wStroke *= 1.8;   // continue building
                    wEdge *= 1.5;
                    wPsych *= 0.2;    // don't chain psych
                    wSurprise *= 0.3;
                    break;
            }
        }

        // ── Early session bias ──
        if (_stepNumber <= 2 && !_punishmentMode)
        {
            wStroke *= 3.0;    // go straight into stroking
            wTease *= 1.5;
            wEdge *= 0.2;
            wTorment *= 0.2;
            wSurprise *= 0.0;  // no surprises in first 2 steps
            wGame *= 0.0;       // no games in first 2 steps
            wPsych *= 0.0;
        }
        else if (_stepNumber <= 1 && _punishmentMode)
        {
            // Brief warm-up even in punishment
            wStroke *= 1.5;
            wTease *= 1.5;
            wSurprise *= 0.0;
            wGame *= 0.0;
            wPsych *= 0.0;
        }

        // Weighted random pick
        if (!Policy.GamesEnabled) wGame = 0;
        if (Policy.Pacing == "Free") { wPsych = 0; wSurprise = 0; wTorment = 0; }
        var pool = new (DirectiveCategory cat, double w)[]
        {
            (DirectiveCategory.Tease, wTease),
            (DirectiveCategory.Stroke, wStroke),
            (DirectiveCategory.Edge, wEdge),
            (DirectiveCategory.Rest, wRest),
            (DirectiveCategory.Torment, wTorment),
            (DirectiveCategory.Surprise, wSurprise),
            (DirectiveCategory.Game, wGame),
            (DirectiveCategory.Psych, wPsych),
        };

        double total = pool.Sum(p => p.w);
        double roll = Random.Shared.NextDouble() * total;
        double cumulative = 0;
        foreach (var (cat, w) in pool)
        {
            cumulative += w;
            if (roll <= cumulative) return cat;
        }
        return DirectiveCategory.Stroke;
    }

    // ──────────────────── Category builders ────────────────────

    private SessionDirective BuildTease()
    {
        int mean = Param(s => s.Mean, 5);
        var m = PickMethod(intensityCeiling: 4, preferTease: true);
        int speed = Random.Shared.Next(1, 4);

        // Methods with decent closeness can use edge counts even for teasing
        if (m.Closeness >= 4)
        {
            int target = 1;
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Tease, m, speed, target, isEdgeCount: true);
            return new SessionDirective
            {
                Kind = InstructionKind.EdgeCount,
                Method = m, Speed = speed, Tenseness = 1,
                DurationOrCount = target,
                Text = DommeVoice.ComposeTease(ctx),
                Phase = SessionPhase.Teasing
            };
        }

        // Low-closeness tease methods use timed (frustrating, won't reach edge)
        int dur = Random.Shared.Next(30, 90);
        {
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Tease, m, speed, dur);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = speed, Tenseness = 1,
                DurationOrCount = dur,
                Text = DommeVoice.ComposeTease(ctx),
                Phase = SessionPhase.Teasing
            };
        }
    }

    private SessionDirective BuildStroke()
    {
        int intensity = Param(s => s.Intensity, 5);
        int mean = Param(s => s.Mean, 5);
        int ceiling = Math.Clamp((int)(intensity * (_arousal / 100.0 + 0.5)), 2, 10);
        var m = PickMethod(ceiling, preferBasic: true);
        int speed = Random.Shared.Next(3, Math.Min(10, ceiling + 1));

        if (ShouldUseTimed(m))
        {
            int dur = Jitter(Random.Shared.Next(45, 150));
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Stroke, m, speed, dur);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = speed,
                Tenseness = Random.Shared.Next(1, Math.Min(8, intensity)),
                DurationOrCount = dur,
                Text = DommeVoice.ComposeStroke(ctx),
                Phase = SessionPhase.Building
            };
        }

        int target = _punishmentMode
            ? 2 + Random.Shared.Next(3)
            : 1 + Random.Shared.Next(2);

        {
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Stroke, m, speed, target, isEdgeCount: true);
            return new SessionDirective
            {
                Kind = InstructionKind.EdgeCount,
                Method = m, Speed = speed,
                Tenseness = Random.Shared.Next(1, Math.Min(8, intensity)),
                DurationOrCount = target,
                Text = DommeVoice.ComposeStroke(ctx),
                Phase = SessionPhase.Building
            };
        }
    }

    private SessionDirective BuildEdge()
    {
        int intensity = Param(s => s.Intensity, 5);
        int mean = Param(s => s.Mean, 5);
        int ceiling = Math.Min(10, intensity + 1);
        var m = PickMethod(ceiling, preferCloseness: true, preferBasic: true);
        int speed = Random.Shared.Next(4, Math.Min(10, ceiling + 2));

        if (m.Closeness < 3)
        {
            var retry = PickMethod(ceiling, preferCloseness: true, preferBasic: true);
            if (retry.Closeness > m.Closeness) m = retry;
        }

        if (m.Closeness < 2)
        {
            int dur = Jitter(Random.Shared.Next(30, 90));
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Edge, m, speed, dur);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = speed,
                DurationOrCount = dur,
                Text = DommeVoice.ComposeEdge(ctx),
                Phase = SessionPhase.Edging
            };
        }

        int target = _retaliating || _punishmentMode
            ? 3 + Random.Shared.Next(4)
            : 1 + Random.Shared.Next(3);

        int holdSeconds = ComputeHoldSeconds();
        var edgeCtx = BuildSpeechContext(DommeVoice.DirectiveCategory.Edge, m, speed, target, holdSeconds, isEdgeCount: true);
        return new SessionDirective
        {
            Kind = InstructionKind.EdgeCount,
            Method = m, Speed = speed,
            DurationOrCount = target,
            HoldSeconds = holdSeconds,
            Text = DommeVoice.ComposeEdge(edgeCtx),
            Phase = SessionPhase.Edging
        };
    }

    private SessionDirective BuildRest()
    {
        int mean = Param(s => s.Mean, 5);
        int baseDur = mean >= 7 || _punishmentMode ? 10 : 20;
        int dur = baseDur + Random.Shared.Next(5, 20);
        dur = Math.Max(5, (int)(dur * (1.5 - mean / 10.0)));
        if (_punishmentMode) dur = Math.Max(5, dur / 2);

        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Rest, durationOrCount: dur);
        return new SessionDirective
        {
            Kind = InstructionKind.Rest,
            DurationOrCount = dur,
            Text = DommeVoice.ComposeRest(ctx),
            Phase = SessionPhase.Resting
        };
    }

    private SessionDirective BuildTorment()
    {
        int intensity = Param(s => s.Intensity, 5);
        int ceiling = Math.Min(10, intensity + 3);
        var m = PickMethod(ceiling, preferTorture: true);
        int speed = Random.Shared.Next(6, 10);
        int dur = _punishmentMode
            ? Jitter(Random.Shared.Next(45, 120))
            : Jitter(Random.Shared.Next(30, 90));

        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Torment, m, speed, dur);
        return new SessionDirective
        {
            Kind = InstructionKind.TimedMethod,
            Method = m, Speed = speed,
            Tenseness = Random.Shared.Next(6, 10),
            DurationOrCount = dur,
            Text = DommeVoice.ComposeTorment(ctx),
            Phase = SessionPhase.Tormenting
        };
    }

    private SessionDirective BuildGameOnlyConsequence(bool won, GameType type)
    {
        if (won)
        {
            // Win reward: short rest with a brief praise note
            string winText = type switch
            {
                GameType.NumberGuess    => "Correct guess. You earned a short break.",
                GameType.CoinFlip       => "Good call. Enjoy a moment of rest.",
                GameType.DoubleOrNothing => "Bold play, correct call. Take a breather.",
                GameType.PickYourPoison => "You chose wisely. Rest now.",
                GameType.QuickMath      => "Right answer. Rest as your reward.",
                GameType.MemorySequence => "Good memory. Take a short rest.",
                GameType.EdgeRush       => "Nice count. You get a rest.",
                GameType.EdgeCounter    => "Right on the mark. Your reward is rest.",
                GameType.RerollGamble   => "Smart gamble. Rest for it.",
                GameType.CardDraw       => "Favorable draw. Enjoy the rest.",
                GameType.RaceToEdge     => "You made it in time. Rest.",
                GameType.ArousalChallenge => "You held it together. Rest.",
                GameType.EdgeEndurance  => "You lasted. Rest now.",
                GameType.RedLightGreenLight => "You stopped when told. Rest.",
                _ => "Well done. Short rest as your reward."
            };
            int restDur = 15 + Random.Shared.Next(15);
            return new SessionDirective
            {
                Kind = InstructionKind.Rest,
                DurationOrCount = restDur,
                Text = winText,
                Phase = SessionPhase.Resting
            };
        }
        else
        {
            // Loss punishment: torment directive with game-specific context
            _retaliating = true;
            _retaliationStepsLeft = 1 + Random.Shared.Next(2);
            int intensity = Param(s => s.Intensity, 5);
            int ceiling = Math.Min(10, intensity + 2);
            var m = PickMethod(ceiling, preferTorture: true);
            int speed = Random.Shared.Next(6, 10);
            int dur = Jitter(Random.Shared.Next(30, 75));
            string lossText = type switch
            {
                GameType.NumberGuess    => $"Wrong guess. {dur} seconds of torment for that.",
                GameType.CoinFlip       => $"Wrong call. {dur} seconds for doubting yourself.",
                GameType.DoubleOrNothing => $"Greedy and wrong. {dur} seconds.",
                GameType.PickYourPoison => $"Bad choice. {dur} seconds of the consequences.",
                GameType.QuickMath      => $"Wrong answer. {dur} seconds for that failure.",
                GameType.MemorySequence => $"You forgot. {dur} seconds to help you remember.",
                GameType.EdgeRush       => $"You missed the count. {dur} seconds.",
                GameType.EdgeCounter    => $"Wrong edge count. {dur} seconds for that.",
                GameType.RerollGamble   => $"Gambled and lost. {dur} seconds.",
                GameType.CardDraw       => $"Poor draw. {dur} seconds.",
                GameType.RaceToEdge     => $"Too slow. {dur} seconds.",
                GameType.ArousalChallenge => $"Couldn't hold it. {dur} seconds.",
                GameType.EdgeEndurance  => $"Didn't last. {dur} seconds.",
                GameType.RedLightGreenLight => $"You moved when I said stop. {dur} seconds for disobeying.",
                _ => $"You failed. {dur} seconds of punishment."
            };
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = speed,
                Tenseness = Random.Shared.Next(6, 10),
                DurationOrCount = dur,
                Text = lossText,
                Phase = SessionPhase.Tormenting
            };
        }
    }

    private SessionDirective BuildSurprise()
    {
        int intensity = Param(s => s.Intensity, 5);
        int roll = Random.Shared.Next(3);
        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Surprise);

        if (roll == 0)
        {
            int dur = Random.Shared.Next(10, 25);
            return new SessionDirective
            {
                Kind = InstructionKind.FreeText,
                DurationOrCount = dur,
                Text = DommeVoice.ComposeSurpriseStop(ctx, dur),
                Phase = SessionPhase.Intense
            };
        }

        if (roll == 1)
        {
            // Speed burst
            var m = PickMethod(10);
            int dur = Random.Shared.Next(10, 25);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = Random.Shared.Next(8, 10),
                DurationOrCount = dur,
                Text = DommeVoice.ComposeSurpriseBurst(ctx, m, dur),
                Phase = SessionPhase.Intense
            };
        }

        // Hard method swap
        var hard = PickMethod(Math.Min(10, intensity + 3), preferTorture: true);
        int swapDur = Random.Shared.Next(30, 60);
        int swapSpeed = Random.Shared.Next(6, 10);
        return new SessionDirective
        {
            Kind = InstructionKind.TimedMethod,
            Method = hard, Speed = swapSpeed,
            DurationOrCount = swapDur,
            Text = DommeVoice.ComposeSurpriseSwap(ctx, hard, swapSpeed, swapDur),
            Phase = SessionPhase.Intense
        };
    }

    private SessionDirective BuildGame()
    {
        int mean = Param(s => s.Mean, 5);
        int intensity = Param(s => s.Intensity, 5);

        if (_gamesOnlyMode)
        {
            var allTypes = Enum.GetValues<GameType>()
                .Where(type => !_recentGameTypes.Contains(type))
                .ToArray();
            var available = allTypes.Length > 0 ? allTypes : Enum.GetValues<GameType>();
            var forcedType = available[Random.Shared.Next(available.Length)];

            if (_recentGameTypes.Count >= 5)
                _recentGameTypes.Dequeue();
            _recentGameTypes.Enqueue(forcedType);

            return BuildGameDirective(forcedType, mean, intensity);
        }

        // Build a weighted pool of game types based on mood / arousal / mean
        var pool = new List<(GameType type, int weight)>();

        // Always available — mental challenges
        pool.Add((GameType.CoinFlip, 10));
        pool.Add((GameType.NumberGuess, 8));
        pool.Add((GameType.QuickMath, 8));
        pool.Add((GameType.CardDraw, 8));
        pool.Add((GameType.DoubleOrNothing, 6));

        // Memory gets harder when mean — add at moderate+
        if (mean >= 4) pool.Add((GameType.MemorySequence, 7));

        // Edge-based challenges — only when arousal is building
        if (_arousal > 30)
        {
            pool.Add((GameType.EdgeCounter, 8));
            pool.Add((GameType.RaceToEdge, 6));
        }
        if (_arousal > 50)
        {
            pool.Add((GameType.EdgeEndurance, 7));
            pool.Add((GameType.RedLightGreenLight, 6));
        }

        // High arousal + mean → dangerous arousal challenge
        if (_arousal > 60 && mean >= 5)
            pool.Add((GameType.ArousalChallenge, 10));

        // Edge rush — moderate+ intensity
        if (intensity >= 4)
            pool.Add((GameType.EdgeRush, 6));

        // Pick your poison — mean domme only
        if (mean >= 6)
            pool.Add((GameType.PickYourPoison, mean >= 8 ? 12 : 6));

        // Reroll gamble — end-game / high arousal
        if (_arousal > 70)
            pool.Add((GameType.RerollGamble, 5));

        // Weighted random selection
        int totalWeight = pool.Sum(p => p.weight);
        int roll = Random.Shared.Next(totalWeight);
        GameType gameType = GameType.CoinFlip;
        int acc = 0;
        foreach (var (type, weight) in pool)
        {
            acc += weight;
            if (roll < acc) { gameType = type; break; }
        }

        return BuildGameDirective(gameType, mean, intensity);
    }

    private SessionDirective BuildGameDirective(GameType gameType, int mean, int intensity)
    {
        if (_recentGameTypes.Count >= 5)
            _recentGameTypes.Dequeue();
        _recentGameTypes.Enqueue(gameType);
        _gamePhase = 0;
        _gamePhaseData = 0;
        _gamePhaseData2 = 0;

        // Create the challenge (fire-and-forget the async DB write)
        GameChallenge? challenge = null;
        try
        {
            challenge = _gameEngine.CreateChallengeAsync(_sessionId, gameType).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create game challenge, falling back to stroke");
            return BuildStroke();
        }

        _activeGameChallengeId = challenge.Id;
        _activeGameType = gameType;

        switch (gameType)
        {
            // ── ArousalChallenge: timed stroke, edge = auto-lose, timer expiry = win ──
            case GameType.ArousalChallenge:
            {
                var m = PickMethod(Math.Min(10, intensity + 1), preferBasic: true);
                int dur = 30 + (10 - mean) * 5;
                _activeGameRange = 0;
                _activeGameAnswerReady = false; // no answer buttons — edge detection handles it
                return new SessionDirective
                {
                    Kind = InstructionKind.TimedMethod,
                    Method = m, Speed = Random.Shared.Next(7, 10),
                    DurationOrCount = dur,
                    Text = challenge.Prompt + $" {Desc(m)}. {dur} seconds.",
                    Phase = SessionPhase.Intense
                };
            }

            // ── EdgeEndurance: get to edge first (no timer), then hold timer starts on edge press ──
            case GameType.EdgeEndurance:
            {
                // Pick a method with closeness >= 4 so it's realistic to reach the edge
                var m = PickMethod(Math.Min(10, intensity + 1), preferCloseness: true);
                for (int i = 0; i < 3 && m.Closeness < 4; i++)
                    m = PickMethod(Math.Min(10, intensity + 1), preferCloseness: true);
                int holdSec = 10 + mean * 2;
                _activeGameRange = holdSec; // store hold duration for RecordEdgeAsync
                _activeGameAnswerReady = false; // no buttons yet — user presses Edge first
                _gamePhase = 0; // phase 0 = getting to edge
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0, // no timer — user works toward the edge
                    Text = $"Get to the edge. Use {Desc(m)}. Once you're there, press the Edge button. Then hold it for {holdSec} seconds without going over. If you have to stop to avoid going over, press the button to admit it.",
                    Phase = SessionPhase.Intense
                };
            }

            // ── RedLightGreenLight: multi-phase game managed by timer ──
            case GameType.RedLightGreenLight:
            {
                int rounds = 3 + mean / 3;
                _activeGameRange = 0;
                _activeGameAnswerReady = false; // no answer until all rounds done
                _gamePhase = 0; // will increment on each timer tick
                _gamePhaseData = 0; // current round tracking (via phase)
                _gamePhaseData2 = rounds; // total rounds
                // Start with first green light
                int greenDur = 5 + Random.Shared.Next(10);
                _gamePhase = 1; // phase 1 = first green
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = greenDur,
                    Text = $"Red light, green light. {rounds} rounds. GREEN LIGHT — stroke! Go fast!",
                    Phase = SessionPhase.Intense
                };
            }

            // ── EdgeRush: timed, user edges during timer, reports count after ──
            case GameType.EdgeRush:
            {
                int timedDur = Math.Max(25, 115 - intensity * 7 - mean * 2);
                _activeGameRange = 0;
                _activeGameAnswerReady = false; // can't answer during edge phase
                _gamePhase = 0; // phase 0 = edging
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = timedDur,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }

            // ── RaceToEdge: timed, user presses Edge to indicate success ──
            case GameType.RaceToEdge:
            {
                int timedDur = 60 - mean * 3;
                timedDur = Math.Max(20, timedDur);
                _activeGameRange = 0;
                _activeGameAnswerReady = false; // success is reported by pressing Edge
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = timedDur,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }

            // ── CardDraw: user predicts high or low, then card is revealed ──
            case GameType.CardDraw:
            {
                _activeGameRange = challenge.CorrectAnswer; // card value for resolution
                _activeGameAnswerReady = true; // show high/low buttons
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }

            // ── EdgeCounter: user edges until stopped, then reports count ──
            case GameType.EdgeCounter:
            {
                _activeGameRange = challenge.CorrectAnswer;
                _activeGameAnswerReady = false;
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }

            // ── MemorySequence: show digits briefly, then ask for recall ──
            case GameType.MemorySequence:
            {
                int memorizeSec = Math.Max(2, 4 - mean / 4); // mean=1→4s, mean=10→2s
                _activeGameRange = -1; // free numeric input
                _activeGameAnswerReady = false; // can't answer during memorization
                _gamePhase = 0; // phase 0 = memorizing
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = memorizeSec,
                    Text = challenge.Prompt + $" You have {memorizeSec} seconds to memorize — then they disappear.",
                    Phase = SessionPhase.Intense
                };
            }

            // ── QuickMath: input with answer timer ──
            case GameType.QuickMath:
            {
                int answerTimeSec = Math.Max(8, mean >= 8 ? 12 : 22 - mean);
                _activeGameRange = -1; // free numeric input
                _activeGameAnswerReady = true;
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = answerTimeSec,
                    Text = challenge.Prompt + $" You have {answerTimeSec} seconds.",
                    Phase = SessionPhase.Intense
                };
            }

            // ── RerollGamble: can reroll multiple times ──
            case GameType.RerollGamble:
            {
                _activeGameRange = 1; // 0=accept, 1=reroll
                _activeGameAnswerReady = true;
                _gamePhaseData = 0; // reroll count
                _gamePhaseData2 = 3 + Random.Shared.Next(3); // max rerolls (3-5)
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }

            // ── Standard interactive games: NumberGuess, CoinFlip, DoubleOrNothing, PickYourPoison ──
            default:
            {
                _activeGameAnswerReady = true;
                _activeGameRange = gameType switch
                {
                    GameType.NumberGuess => 4 + mean / 2,
                    GameType.CoinFlip => 1,
                    GameType.DoubleOrNothing => 1,
                    GameType.PickYourPoison => 2,
                    _ => 1
                };
                return new SessionDirective
                {
                    Kind = InstructionKind.FreeText,
                    DurationOrCount = 0,
                    Text = challenge.Prompt,
                    Phase = SessionPhase.Intense
                };
            }
        }
    }

    private SessionDirective BuildPsych()
    {
        int mean = Param(s => s.Mean, 5);
        int roll = Random.Shared.Next(10);

        // False promise setup (only if not already active)
        if (roll < 5 && !_falsePromiseActive && _arousal > 40)
        {
            _falsePromiseActive = true;
            _falsePromiseStepsLeft = 2 + Random.Shared.Next(3); // reveal in 2-4 steps
            _logger.LogInformation("Session {Id}: false promise activated ({Steps} steps)",
                _sessionId, _falsePromiseStepsLeft);

            // Return a normal stroke step but with promise text
            return BuildStroke();
        }

        // Loyalty test — psychological pressure paired with a stroke instruction
        if (roll < 8)
        {
            var m = PickMethod(Math.Min(10, Param(s => s.Intensity, 5)), preferBasic: true);
            int dur = Jitter(Random.Shared.Next(30, 60));
            int speed = Random.Shared.Next(3, 7);
            var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Psych, m, speed, dur);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = speed,
                DurationOrCount = dur,
                Text = DommeVoice.ComposePsychLoyalty(ctx, m, speed, dur),
                Phase = SessionPhase.Building
            };
        }

        // Denial taunt — psychological pressure
        var m2 = PickMethod(Math.Min(10, Param(s => s.Intensity, 5)), preferBasic: true);
        int dur2 = Jitter(Random.Shared.Next(30, 60));
        int speed2 = Random.Shared.Next(3, 7);
        var ctx2 = BuildSpeechContext(DommeVoice.DirectiveCategory.Psych, m2, speed2, dur2);
        return new SessionDirective
        {
            Kind = InstructionKind.TimedMethod,
            Method = m2, Speed = speed2,
            DurationOrCount = dur2,
            Text = DommeVoice.ComposePsychDenialTaunt(ctx2, m2, speed2, dur2),
            Phase = SessionPhase.Building
        };
    }

    private SessionDirective BuildAutoFinish()
    {
        // Compute cum chance using the session's denial level + edges
        int denial = Param(s => s.Denial, 5);
        // Base chance: lower denial = higher chance. denial=1→80%, denial=5→30%, denial=10→5%
        double baseCumChance = Math.Max(5, 90 - denial * 8.5);
        // Bonus for high edge counts
        double edgeBonus = Math.Min(20, _edgesThisSession * 0.8);
        double cumChance = Math.Clamp(baseCumChance + edgeBonus, 0, 95);

        bool grantPermission = RandomHelper.PercentChance(cumChance);
        _logger.LogInformation("Session {Id} auto-finish: cumChance={Chance:N1}%, granted={Grant}",
            _sessionId, cumChance, grantPermission);

        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Stroke);

        if (grantPermission)
        {
            int intensity = Param(s => s.Intensity, 5);
            var m = PickMethod(Math.Min(10, intensity), preferBasic: true);
            int dur = Random.Shared.Next(30, 90);
            return new SessionDirective
            {
                Kind = InstructionKind.TimedMethod,
                Method = m, Speed = Random.Shared.Next(5, 10),
                DurationOrCount = dur,
                Text = DommeVoice.ComposeAutoFinishGranted(ctx, m, dur),
                Phase = SessionPhase.Finishing
            };
        }
        else
        {
            // Don't set _ended yet — let the Finish directive display first.
            // Session ends when the user acknowledges via CompleteStepAsync.
            return new SessionDirective
            {
                Kind = InstructionKind.Finish,
                DurationOrCount = 0,
                Text = DommeVoice.ComposeAutoFinishDenied(ctx),
                Phase = SessionPhase.Finishing
            };
        }
    }

    private SessionDirective BuildFalsePromiseReveal()
    {
        int intensity = Param(s => s.Intensity, 5);
        var m = PickMethod(Math.Min(10, intensity), preferTorture: true);
        int extra = Jitter(Random.Shared.Next(60, 180));

        // Trigger retaliation after the reveal — the hope was the cruelty
        _retaliating = true;
        _retaliationReason = RetaliationReason.BegCumDenied;
        _retaliationStepsLeft = 2 + Random.Shared.Next(3);
        _logger.LogInformation("Session {Id}: false promise reveal — retaliation ON ({Steps} steps)",
            _sessionId, _retaliationStepsLeft);

        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Psych, m, Random.Shared.Next(5, 9), extra);
        return new SessionDirective
        {
            Kind = InstructionKind.TimedMethod,
            Method = m, Speed = ctx.Speed,
            Tenseness = Random.Shared.Next(5, 10),
            DurationOrCount = extra,
            Text = DommeVoice.ComposeFalsePromiseReveal(ctx, m, extra),
            Phase = SessionPhase.Tormenting
        };
    }

    private SessionDirective EndDirective(bool wasOrgasmGranted = false)
    {
        var ctx = BuildSpeechContext(DommeVoice.DirectiveCategory.Stroke);
        return new()
        {
            Kind = InstructionKind.FreeText,
            DurationOrCount = 0,
            Text = DommeVoice.ComposeSessionEnd(ctx, wasOrgasmGranted),
            Phase = SessionPhase.Ended
        };
    }

    // ──────────────────── Arousal tracking ────────────────────

    private void UpdateArousalForCompletedStep()
    {
        if (_currentDirective is null) return;

        switch (_currentDirective.Kind)
        {
            case InstructionKind.TimedMethod:
            {
                double closeness = _currentDirective.Method?.Closeness ?? 3;
                double speedFactor = _currentDirective.Speed / 10.0;
                double durFactor = Math.Min(_currentDirective.DurationOrCount / 60.0, 2.5);
                _arousal = Math.Min(95, _arousal + closeness * speedFactor * durFactor * 5.0);
                break;
            }
            case InstructionKind.EdgeCount:
                // edges already tracked in RecordEdgeAsync
                break;
            case InstructionKind.Rest:
                _arousal = Math.Max(0, _arousal - _currentDirective.DurationOrCount * 0.4);
                break;
            case InstructionKind.FreeText:
                // surprise stops: small decay
                _arousal = Math.Max(0, _arousal - 5);
                break;
        }
    }

    // ──────────────────── Stimulation point tracking ────────────────────

    /// <summary>
    /// Award stimulation points based on the just-completed step.
    /// Points drive session progression instead of wall-clock time.
    /// </summary>
    private void AwardStimPoints()
    {
        if (_currentDirective is null) return;

        int pts = 0;
        switch (_currentDirective.Kind)
        {
            case InstructionKind.EdgeCount:
                if (_currentDirective.Method?.EdgePointMultiplier > 0)
                {
                    pts = Math.Max(1, (int)Math.Round(_currentDirective.DurationOrCount * _currentDirective.Method.EdgePointMultiplier));
                }
                else
                {
                    pts = StimMethodPointMath.EstimateLegacyEdgePoints(_currentDirective.DurationOrCount);
                }
                if (_currentDirective.HoldSeconds > 0)
                    pts += _currentDirective.HoldSeconds / 5 + 1;
                break;

            case InstructionKind.TimedMethod:
            {
                int dur = _currentDirective.DurationOrCount;
                if (_currentDirective.Method?.TimePointMultiplier > 0)
                {
                    pts = Math.Max(1, (int)Math.Round(dur * _currentDirective.Method.TimePointMultiplier));
                }
                else if (_currentDirective.Method is not null)
                {
                    pts = StimMethodPointMath.EstimateLegacyTimedPoints(_currentDirective.Method, dur);
                }
                else
                {
                    pts = Math.Max(1, dur / 20);
                }
                break;
            }

            case InstructionKind.Rest:
                // Rest awards 0 stim points (it's rest, not stimulation)
                break;

            case InstructionKind.FreeText:
                // Surprise stops etc. — minimal points
                pts = 1;
                break;
        }

        _sessionPoints += pts;
    }

    // ──────────────────── State derivation ────────────────────

    private SessionPhase DerivePhase()
    {
        if (_ended) return SessionPhase.Ended;
        if (_currentDirective is not null) return _currentDirective.Phase;
        if (_arousal < 25) return SessionPhase.Teasing;
        if (_arousal < 55) return SessionPhase.Building;
        if (_arousal >= 75) return SessionPhase.Edging;
        return SessionPhase.Intense;
    }

    private double SessionProgress()
    {
        if (_stimBudget <= 0) return 0;
        return (double)_sessionPoints / _stimBudget;
    }

    // ──────────────────── Utility ────────────────────

    private int Param(Func<SessionRecord, int> selector, int fallback)
    {
        if (_session is null) return fallback;
        int v = selector(_session);
        return v > 0 ? v : fallback;
    }

    /// <summary>
    /// Torturous and frustrating tease methods use timed duration ("for X seconds").
    /// Everything else uses edge counts (speed + how many edges).
    /// </summary>
    private static bool ShouldUseTimed(StimMethod m)
    {
        // Torturous with low closeness → endure the torture for X seconds
        if (m.TortLevel >= 5 && m.Closeness <= 3) return true;
        // Frustrating tease with low closeness → won't reach edge, time-based
        if (m.Tease >= 6 && m.Closeness <= 3) return true;
        return false;
    }

    /// <summary>
    /// Determines how long to hold at the edge after completing an edge step.
    /// Returns 0 when no hold is required.
    /// </summary>
    // ── Configurable edge hold limits ──
    private const int MinHoldSecondsNormal = 5;
    private const int MaxHoldSecondsNormal = 20;
    private const int MinHoldSecondsPunishment = 8;
    private const int MaxHoldSecondsPunishment = 25;

    private int ComputeHoldSeconds()
    {
        int mean = Param(s => s.Mean, 5);

        // Not every edge step has a hold
        int holdChance = _punishmentMode ? 60 : 35;
        if (Random.Shared.Next(100) >= holdChance)
            return 0;

        int minH = _punishmentMode ? MinHoldSecondsPunishment : MinHoldSecondsNormal;
        int maxH = _punishmentMode ? MaxHoldSecondsPunishment : MaxHoldSecondsNormal;
        int hold = minH + Random.Shared.Next(maxH - minH + 1);
        // Slight mean scaling — cap at max
        hold = Math.Min(maxH, (int)(hold * (1.0 + mean / 40.0)));
        return hold;
    }

    private static int Jitter(int value)
    {
        double factor = 1.0 + (Random.Shared.NextDouble() - 0.5) * 0.4;
        return Math.Max(10, (int)(value * factor));
    }

    // ──────────────────── Domme mood helpers ────────────────────

    private static DommeMood PickInitialMood(bool punishment)
    {
        if (punishment)
        {
            // Punishment sessions start harsh
            return Random.Shared.Next(3) switch
            {
                0 => DommeMood.Cruel,
                1 => DommeMood.Relentless,
                _ => DommeMood.Edgy
            };
        }

        // Normal: weighted toward Neutral, but can start with personality
        int roll = Random.Shared.Next(100);
        if (roll < 35) return DommeMood.Neutral;
        if (roll < 50) return DommeMood.Teasing;
        if (roll < 65) return DommeMood.Edgy;
        if (roll < 78) return DommeMood.Relentless;
        if (roll < 90) return DommeMood.Cruel;
        return DommeMood.Playful;
    }

    /// <summary>
    /// Called each step — counts down the mood timer and may naturally drift
    /// to a different mood. Mean/progress/arousal influence what moods are likely next.
    /// </summary>
    private void MaybeDriftMood()
    {
        _moodStepsRemaining--;
        if (_moodStepsRemaining > 0) return;

        int mean = Param(s => s.Mean, 5);
        double progress = SessionProgress();

        // Weighted pool for the next mood based on context
        double wNeutral = 15;
        double wCruel = 5 + mean * 2;                // meaner → more likely cruel
        double wEdgy = 15 + (_arousal > 50 ? 15 : 0); // high arousal → edgy mood
        double wTeasing = 15 + (progress < 0.3 ? 10 : 0); // early session → more teasing
        double wPlayful = 8;
        double wRelentless = 10 + (progress > 0.5 ? 10 : 0); // later → relentless push

        // Bias away from the current mood (variety)
        switch (_mood)
        {
            case DommeMood.Neutral: wNeutral *= 0.3; break;
            case DommeMood.Cruel: wCruel *= 0.4; break;
            case DommeMood.Edgy: wEdgy *= 0.4; break;
            case DommeMood.Teasing: wTeasing *= 0.4; break;
            case DommeMood.Playful: wPlayful *= 0.3; break;
            case DommeMood.Relentless: wRelentless *= 0.4; break;
        }

        // Punishment sessions suppress soft moods
        if (_punishmentMode)
        {
            wTeasing *= 0.3;
            wNeutral *= 0.3;
            wPlayful *= 0.2;
            wCruel *= 2.0;
        }

        var pool = new (DommeMood m, double w)[]
        {
            (DommeMood.Neutral, wNeutral),
            (DommeMood.Cruel, wCruel),
            (DommeMood.Edgy, wEdgy),
            (DommeMood.Teasing, wTeasing),
            (DommeMood.Playful, wPlayful),
            (DommeMood.Relentless, wRelentless),
        };

        double total = pool.Sum(p => p.w);
        double roll = Random.Shared.NextDouble() * total;
        double cumulative = 0;
        DommeMood next = DommeMood.Neutral;
        foreach (var (m, w) in pool)
        {
            cumulative += w;
            if (roll <= cumulative) { next = m; break; }
        }

        _mood = next;
        _moodStepsRemaining = 4 + Random.Shared.Next(6); // 4-9 steps in new mood
        _moodIntensity = 0.4 + Random.Shared.NextDouble() * 0.5; // 0.4-0.9
        _logger.LogInformation("Session {Id}: mood drifted to {Mood} (intensity={Int:N2}, duration={Steps})",
            _sessionId, _mood, _moodIntensity, _moodStepsRemaining);
    }

    /// <summary>
    /// Interaction-driven mood shift. Positive interactions (good edge performance,
    /// game wins) soften mood; negative interactions (begging, failures) intensify.
    /// </summary>
    private void NudgeMood(double delta)
    {
        // delta > 0 → angrier/meaner, delta < 0 → softer
        _moodIntensity = Math.Clamp(_moodIntensity + delta * 0.15, 0.1, 1.0);

        // Strong anger can flip mood to Cruel or Relentless
        if (delta > 0.5 && _mood is DommeMood.Neutral or DommeMood.Teasing or DommeMood.Playful)
        {
            if (Random.Shared.Next(100) < 40)
            {
                _mood = Random.Shared.Next(2) == 0 ? DommeMood.Cruel : DommeMood.Relentless;
                _moodStepsRemaining = 3 + Random.Shared.Next(4);
                _moodIntensity = Math.Max(_moodIntensity, 0.6);
                _logger.LogInformation("Session {Id}: mood snapped to {Mood} from interaction",
                    _sessionId, _mood);
            }
        }

        // Strong positive can soften out of Cruel
        if (delta < -0.5 && _mood is DommeMood.Cruel)
        {
            if (Random.Shared.Next(100) < 30)
            {
                _mood = Random.Shared.Next(2) == 0 ? DommeMood.Edgy : DommeMood.Neutral;
                _moodStepsRemaining = 3 + Random.Shared.Next(3);
                _logger.LogInformation("Session {Id}: mood softened to {Mood} from interaction",
                    _sessionId, _mood);
            }
        }
    }

    /// <summary>Get description text suitable for embedding — strips trailing punctuation so callers can add their own.</summary>
    private static string Desc(StimMethod m)
    {
        string d = m.Description.TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(d) ? m.Name : d;
    }

    /// <summary>Build a SpeechContext snapshot of current engine state for DommeVoice.</summary>
    private DommeVoice.SpeechContext BuildSpeechContext(
        DommeVoice.DirectiveCategory category,
        StimMethod? method = null,
        int speed = 0,
        int durationOrCount = 0,
        int holdSeconds = 0,
        bool isEdgeCount = false)
    {
        return new DommeVoice.SpeechContext
        {
            Mood = (DommeVoice.DommeMood)(int)_mood,
            MoodIntensity = _moodIntensity,
            Arousal = _arousal,
            Progress = SessionProgress(),
            StepNumber = _stepNumber,
            EdgesThisSession = _edgesThisSession,
            EdgesThisStep = _edgesThisStep,
            IsRetaliating = _retaliating,
            RetaliationReason = _retaliationReason,
            IsPunishment = _punishmentMode,
            Method = method,
            PreviousCategory = _previousCategory is not null
                ? (DommeVoice.DirectiveCategory)(int)_previousCategory.Value
                : null,
            Category = category,
            Speed = Policy.Pacing == "Free" ? 0 : speed,
            Mean = Param(s => s.Mean, 5),
            DurationOrCount = durationOrCount,
            HoldSeconds = holdSeconds,
            LubeActive = _lubeActive,
            SessionPoints = _sessionPoints,
            FalsePromiseActive = _falsePromiseActive,
            IsEdgeCount = isEdgeCount,
        };
    }

    private StimMethod PickMethod(int intensityCeiling, bool preferTease = false, bool preferTorture = false, bool preferCloseness = false, bool preferBasic = false)
    {
        var pool = _eligibleMethods.Where(m => m.Intensity <= intensityCeiling).ToList();
        if (pool.Count == 0) pool = _eligibleMethods.ToList();
        if (pool.Count == 0) return FallbackMethod();

        // Hard lube enforcement: once lube is active, only pick lube methods.
        // If no lube methods satisfy the intensity ceiling, expand to all eligible lube methods.
        if (_lubeActive)
        {
            var lubePool = pool.Where(m => m.RequiresLube).ToList();
            if (lubePool.Count == 0)
                lubePool = _eligibleMethods.Where(m => m.RequiresLube).ToList();
            if (lubePool.Count > 0) pool = lubePool;
        }

        // Method chaining: when a chain is active, strongly prefer the current
        // chain method for natural flow (same grip for several steps before switching).
        // Only chain basic/common methods — niche methods (high tease or low closeness)
        // feel repetitive when chained.
        if (_chainMethod is not null && _chainStepsRemaining > 0 && !preferTorture
            && _chainMethod.Closeness >= 3 && _chainMethod.Tease < 6)
        {
            if (pool.Any(m => m.Id == _chainMethod.Id) && Random.Shared.Next(100) < 55)
            {
                _chainStepsRemaining--;
                TrackMethod(_chainMethod);
                return _chainMethod;
            }
        }

        // Identify the last used method to strongly avoid immediate repeats
        int lastMethodId = _recentMethodIds.Count > 0 ? _recentMethodIds[^1] : -1;

        var weighted = pool.Select(m =>
        {
            int idx = _recentMethodIds.LastIndexOf(m.Id);
            double w = idx >= 0 ? 1.0 / (1 + (_recentMethodIds.Count - idx)) : 2.0;
            // Near-zero weight for the method we just used (avoid back-to-back repeats)
            if (m.Id == lastMethodId && pool.Count > 1) w *= 0.02;
            if (preferTease) w *= 1.0 + m.Tease / 10.0;
            if (preferTorture) w *= 1.0 + m.TortLevel / 10.0;
            if (preferCloseness) w *= 1.0 + m.Closeness / 5.0;
            // Basic stroking preference: boost high-closeness, low-exotic methods
            if (preferBasic)
            {
                double basicScore = Math.Max(0.1, (m.Closeness / 10.0) * (1.0 - m.Tease / 15.0) * (1.0 - m.TortLevel / 15.0));
                w *= 1.0 + basicScore * 4.0;
            }
            // If lube is NOT active, mildly suppress lube methods (avoid forcing lube early)
            // Note: _lubeActive=true is handled via hard filter above — pool only contains lube methods
            if (!_lubeActive && m.RequiresLube) w *= 0.4;
            return (m, w);
        }).ToList();

        double total = weighted.Sum(x => x.w);
        double roll = Random.Shared.NextDouble() * total;
        double cum = 0;
        foreach (var (m, w) in weighted)
        {
            cum += w;
            if (roll <= cum) { TrackMethod(m); return m; }
        }

        var fb = pool[Random.Shared.Next(pool.Count)];
        TrackMethod(fb);
        return fb;
    }

    private void TrackMethod(StimMethod method)
    {
        _recentMethodIds.Add(method.Id);
        if (_recentMethodIds.Count > 10)
            _recentMethodIds.RemoveAt(0);
        if (method.RequiresLube) _lubeActive = true;
    }

    private static StimMethod FallbackMethod() => new()
    {
        Id = 0,
        Name = "Standard stroke",
        Description = "Use your dominant hand at a comfortable pace."
    };

    private void LogDirective(SessionDirective d)
    {
        _logger.LogInformation(
            "Session {Id} step {N}: [{Phase}] {Kind} — arousal={Arousal:N0} — {Text}",
            _sessionId, _stepNumber, d.Phase, d.Kind, _arousal, d.Text);
    }

    private static List<StimMethod> FilterMethodsForSession(List<StimMethod> all, SessionRecord s)
    {
        return all.Where(m =>
        {
            if (m.ToyCategory == ToyCategory.None) return true;
            if (!s.AllowToys) return false;
            return m.ToyCategory switch
            {
                ToyCategory.HeadVibrator => s.AllowVibrator,
                ToyCategory.Fleshlight => s.AllowFleshlight,
                ToyCategory.Autoblow => s.AllowAutoblow,
                ToyCategory.ProstateMassager => s.AllowProstate,
                _ => false
            };
        }).ToList();
    }

    // ──────────────────── Step timer ────────────────────

    private void StartStepTimerIfTimed(SessionDirective directive)
    {
        bool isTimed = directive.Kind is InstructionKind.TimedMethod
                                     or InstructionKind.Rest
                                     or InstructionKind.FreeText;

        if (!isTimed || directive.DurationOrCount <= 0)
        {
            _stepDurationSeconds = 0;
            return;
        }

        _stepStartUtc = DateTime.UtcNow;
        _stepDurationSeconds = directive.DurationOrCount;
        ArmTimer(_stepDurationSeconds);
    }

    private void CancelStepTimer()
    {
        _timerGeneration++;
        _stepTimer?.Dispose();
        _stepTimer = null;
        _stepDurationSeconds = 0;
    }

    private void OnStepTimerElapsed(object? state)
    {
        SessionDirective? next = null;
        lock (_lock)
        {
            if (_ended || _paused || _sessionId == 0 || state is not long generation || generation != _timerGeneration) return;

            // If the cum-permission timer expired → user didn't cum in time → denial
            if (_currentDirective?.Phase == SessionPhase.Finishing)
            {
                CancelStepTimer();
                _logger.LogInformation("Session {Id} cum timer expired — showing denial", _sessionId);
                next = new SessionDirective
                {
                    Kind = InstructionKind.Finish,
                    DurationOrCount = 0,
                    Text = "Time's up. You didn't cum in time. Denied.",
                    Phase = SessionPhase.Finishing
                };
                _currentDirective = PolicyDirective(next);
            }
            else
            {
                CancelStepTimer();
                _holdingAtEdge = false;

                // ── Game-type-specific timer handling ──
                if (_activeGameChallengeId is not null)
                {
                    bool gameHandledNext = false;
                    switch (_activeGameType)
                    {
                        case GameType.EdgeRush when _gamePhase == 0:
                            // Edge phase over — now ask for the count
                            _gamePhase = 1;
                            _activeGameRange = _edgesThisStep;
                            _activeGameAnswerReady = true;
                            next = new SessionDirective
                            {
                                Kind = InstructionKind.FreeText,
                                DurationOrCount = 20, // 20s grace to enter count
                                Text = "Time's up. How many edges did you get? Enter your count now.",
                                Phase = SessionPhase.Intense
                            };
                            _currentDirective = PolicyDirective(next);
                            LogDirective(next);
                            StartStepTimerIfTimed(next);
                            gameHandledNext = true;
                            _logger.LogInformation("Session {Id}: EdgeRush timer expired — awaiting count", _sessionId);
                            break;

                        case GameType.EdgeRush when _gamePhase == 1:
                            AutoResolveGameLocked(answer: -1, won: false);
                            next = new SessionDirective
                            {
                                Kind = InstructionKind.FreeText,
                                DurationOrCount = 0,
                                Text = "Too late. You didn't report your edge count in time. You lose.",
                                Phase = SessionPhase.Tormenting
                            };
                            _currentDirective = PolicyDirective(next);
                            LogDirective(next);
                            gameHandledNext = true;
                            _logger.LogInformation("Session {Id}: EdgeRush answer window expired — auto-loss", _sessionId);
                            break;

                        case GameType.ArousalChallenge:
                            if (_gamePhase == 1)
                            {
                                AutoResolveGameLocked(answer: 1, won: false);
                                _logger.LogInformation("Session {Id}: ArousalChallenge expired after failure — auto-loss", _sessionId);
                            }
                            else
                            {
                                AutoResolveGameLocked(answer: 0, won: true);
                                _logger.LogInformation("Session {Id}: ArousalChallenge survived — auto-win", _sessionId);
                            }
                            break;

                        case GameType.EdgeEndurance when _gamePhase == 1:
                            // Hold timer expired = they held it = WIN
                            AutoResolveGameLocked(answer: 1, won: true);
                            _logger.LogInformation("Session {Id}: EdgeEndurance held — auto-win", _sessionId);
                            break;

                        case GameType.MemorySequence when _gamePhase == 0:
                            // Memorization time over — show answer prompt (remove the sequence)
                            _gamePhase = 1;
                            _activeGameAnswerReady = true;
                            next = new SessionDirective
                            {
                                Kind = InstructionKind.FreeText,
                                DurationOrCount = 30, // 30s to enter the answer
                                Text = "Time's up. What was the sequence? Enter all digits as one number — now.",
                                Phase = SessionPhase.Intense
                            };
                            _currentDirective = PolicyDirective(next);
                            LogDirective(next);
                            StartStepTimerIfTimed(next);
                            gameHandledNext = true;
                            _logger.LogInformation("Session {Id}: MemorySequence memorization over — awaiting answer", _sessionId);
                            break;

                        case GameType.MemorySequence when _gamePhase == 1:
                            AutoResolveGameLocked(answer: -1, won: false);
                            next = new SessionDirective
                            {
                                Kind = InstructionKind.FreeText,
                                DurationOrCount = 0,
                                Text = "Too slow. The sequence is gone, and so is your chance to answer. You lose.",
                                Phase = SessionPhase.Tormenting
                            };
                            _currentDirective = PolicyDirective(next);
                            LogDirective(next);
                            gameHandledNext = true;
                            _logger.LogInformation("Session {Id}: MemorySequence answer window expired — auto-loss", _sessionId);
                            break;

                        case GameType.QuickMath:
                            // Answer timer expired = too slow = auto-lose
                            AutoResolveGameLocked(answer: -1, won: false);
                            next = new SessionDirective
                            {
                                Kind = InstructionKind.FreeText,
                                DurationOrCount = 0,
                                Text = "Too slow. You failed the problem before time ran out.",
                                Phase = SessionPhase.Tormenting
                            };
                            _currentDirective = PolicyDirective(next);
                            LogDirective(next);
                            gameHandledNext = true;
                            _logger.LogInformation("Session {Id}: QuickMath timed out — auto-loss", _sessionId);
                            break;

                        case GameType.RedLightGreenLight:
                        {
                            int totalPhases = _gamePhaseData2 * 2; // each round = green + red
                            if (_gamePhase < totalPhases)
                            {
                                _gamePhase++;
                                bool isGreen = _gamePhase % 2 == 1;
                                int phaseDur = isGreen
                                    ? 5 + Random.Shared.Next(10)   // green: 5-14s
                                    : 3 + Random.Shared.Next(6);   // red: 3-8s
                                string phaseText = isGreen
                                    ? "GREEN LIGHT — stroke! Go fast!"
                                    : "RED LIGHT — STOP! Hands off NOW!";
                                next = new SessionDirective
                                {
                                    Kind = InstructionKind.FreeText,
                                    DurationOrCount = phaseDur,
                                    Text = phaseText,
                                    Phase = SessionPhase.Intense
                                };
                                _currentDirective = PolicyDirective(next);
                                LogDirective(next);
                                StartStepTimerIfTimed(next);
                                gameHandledNext = true;
                            }
                            else
                            {
                                // All rounds complete — ask if they obeyed
                                _activeGameAnswerReady = true;
                                next = new SessionDirective
                                {
                                    Kind = InstructionKind.FreeText,
                                    DurationOrCount = 0,
                                    Text = $"All {_gamePhaseData2} rounds done. Did you obey every command perfectly? Be honest.",
                                    Phase = SessionPhase.Intense
                                };
                                _currentDirective = PolicyDirective(next);
                                LogDirective(next);
                                gameHandledNext = true;
                            }
                            _logger.LogInformation("Session {Id}: RLGL phase {Phase}/{Total}",
                                _sessionId, _gamePhase, totalPhases);
                            break;
                        }

                        default:
                            // Generic game timeout — auto-lose
                            AutoResolveGameLocked(answer: -1, won: false);
                            _logger.LogInformation("Session {Id}: game timed out — auto-loss", _sessionId);
                            break;
                    }

                    if (gameHandledNext)
                    {
                        // The game produced a new directive — skip normal advancement
                        goto timerDone;
                    }
                    // If game was auto-resolved, fall through to normal advancement
                }

                // Resume a suspended timed step after hold/cooldown
                if (_suspendedDirective is not null)
                {
                    var resumeCtx = BuildSpeechContext(DommeVoice.DirectiveCategory.Stroke,
                        _suspendedDirective.Method, _suspendedDirective.Speed, _suspendedRemainingSeconds);
                    var resumed = new SessionDirective
                    {
                        Kind = _suspendedDirective.Kind,
                        Method = _suspendedDirective.Method,
                        Speed = _suspendedDirective.Speed,
                        Tenseness = _suspendedDirective.Tenseness,
                        DurationOrCount = _suspendedRemainingSeconds,
                        HoldSeconds = _suspendedDirective.HoldSeconds,
                        Text = DommeVoice.ComposeResume(resumeCtx, _suspendedDirective.Method!, _suspendedDirective.Speed, _suspendedRemainingSeconds),
                        Phase = _suspendedDirective.Phase
                    };
                    _suspendedDirective = null;
                    _suspendedRemainingSeconds = 0;
                    _currentDirective = PolicyDirective(resumed);
                    LogDirective(resumed);
                    StartStepTimerIfTimed(resumed);
                    next = resumed;
                }
                else
                {
                    UpdateArousalForCompletedStep();
                    _edgesThisStep = 0;
                    next = NextDirectiveLocked();
                }
            }
        }

        timerDone:
        _logger.LogInformation("Step timer elapsed — auto-advanced to next directive");
        if (next is not null)
            StepTimerElapsed?.Invoke(next);
    }
}
