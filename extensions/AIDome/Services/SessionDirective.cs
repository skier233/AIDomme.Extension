using AIDome.Data;

namespace AIDome.Services;

/// <summary>
/// Represents what the user should be doing RIGHT NOW during a live session.
/// This is a transient object — not persisted until the step is completed.
/// </summary>
public sealed class SessionDirective
{
    /// <summary>What kind of step this is.</summary>
    public InstructionKind Kind { get; init; }

    /// <summary>The stim method to use (null for rest / free-text).</summary>
    public StimMethod? Method { get; init; }

    /// <summary>Speed 1-10 (if applicable).</summary>
    public int Speed { get; init; }

    /// <summary>Tenseness 1-10 (if applicable).</summary>
    public int Tenseness { get; init; }

    /// <summary>
    /// For TimedMethod: seconds to perform. For EdgeCount: number of edges.
    /// For Rest: seconds to wait. For Finish: 0 (open-ended).
    /// </summary>
    public int DurationOrCount { get; init; }

    /// <summary>
    /// For EdgeCount steps: seconds to hold at the edge after the last edge is reached.
    /// 0 means no hold — auto-advance immediately.
    /// </summary>
    public int HoldSeconds { get; init; }

    /// <summary>Human-readable instruction text shown to the user.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Descriptive label for the current engine mood/state.</summary>
    public SessionPhase Phase { get; init; }
}

/// <summary>
/// Describes *what the engine is currently doing* rather than a rigid progression.
/// Derived from arousal, mean-level, retaliation state, etc.
/// </summary>
public enum SessionPhase
{
    Teasing = 0,
    Building = 1,
    Intense = 2,
    Edging = 3,
    Resting = 4,
    Tormenting = 5,
    Finishing = 6,
    Ended = 7
}

/// <summary>Events the service can push into the online engine to influence behaviour.</summary>
public enum SessionEvent
{
    BegDenied = 0,
    BegGranted = 1,
    InfractionReported = 2,
    BegStopDenied = 3,
    BegStopGranted = 4,
    OrgasmReported = 5,
    RuinedOrgasmReported = 6
}

/// <summary>Tracks the reason for retaliation so speech text matches context.</summary>
public enum RetaliationReason
{
    BegCumDenied = 0,
    BegStopDenied = 1,
    Disobedience = 2,
    UnauthorizedOrgasm = 3,
    RuinedOrgasm = 4
}

/// <summary>Snapshot of what the online engine knows about the running session.</summary>
public sealed class OnlineSessionState
{
    public bool IsPaused { get; init; }
    public int SessionId { get; init; }
    public SessionPhase Phase { get; init; }
    public SessionDirective? CurrentDirective { get; init; }
    public int StepNumber { get; init; }
    public int EdgesThisSession { get; init; }
    public int ElapsedSeconds { get; init; }
    public int TotalBudgetSeconds { get; init; }
    public int Arousal { get; init; }
    public bool IsFinished { get; init; }
    public int Points { get; init; }

    /// <summary>Debug sessions may be ended by the user; all other sessions can only be paused.</summary>
    public bool IsDebugSession { get; init; }

    /// <summary>Seconds remaining on the current timed step, or -1 if not a timed step.</summary>
    public int RemainingSeconds { get; init; }

    /// <summary>ID of the active game challenge awaiting an answer, or null.</summary>
    public int? ActiveGameChallengeId { get; init; }

    /// <summary>Type of the active game, if any.</summary>
    public GameType? ActiveGameType { get; init; }

    /// <summary>True when the active game is ready for the user to submit an answer.</summary>
    public bool ActiveGameAnswerReady { get; init; }

    /// <summary>For NumberGuess: max number in the range (1..N). For CoinFlip: 1.</summary>
    public int ActiveGameRange { get; init; }

    /// <summary>Current domme mood label (e.g. "Cruel", "Teasing", "Edgy").</summary>
    public string Mood { get; init; } = "Neutral";

    /// <summary>True when the current step is a lube-application pause awaiting user readiness.</summary>
    public bool IsLubePause { get; init; }

    /// <summary>How the session ended (valid when IsFinished is true).</summary>
    public SessionOutcome Outcome { get; init; }
}
