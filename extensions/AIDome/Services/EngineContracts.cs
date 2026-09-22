using AIDome.Data;

namespace AIDome.Services;

/// <summary>
/// Generates a plan of <see cref="InstructionStep"/>s for a session based on its
/// parameters, available stim methods, user history, and active punishments.
///
/// Design notes
/// ────────────
/// A session plan is a list of steps that form an arc:
///   Warm-up → Build → Peak → (optional Finish or Denial)
///
/// <b>Warm-up</b>: Low-intensity / tease methods for a short timed duration.
/// <b>Build</b>:   Progressively harder methods, edge counts that increase, interspersed
///                with rest periods that get shorter as intensity climbs.
/// <b>Peak</b>:    The hardest methods available for the session; longest edge holds.
/// <b>Finish</b>:  If the user is allowed to cum, specifies *how* (method, speed, post-orgasm).
///                If denied, may issue a "stop and cool down" step or a punishment.
///
/// Method selection is filtered by:
///   • Session toy opt-in flags (AllowVibrator, AllowFleshlight, etc.)
///   • Session kink opt-in flags (AllowAnal, AllowBondage, AllowCEI)
///   • Session intensity (methods whose Intensity exceeds the session cap are excluded
///     from warm-up/build but may appear in peak at high intensity sessions)
///
/// Randomness is injected at every stage so sessions feel non-predictable:
///   • Method order is shuffled within each phase.
///   • Duration/edge-count targets have ±20 % jitter.
///   • Occasional "surprise" steps (game challenge, forced method swap) are inserted
///     based on a probability that scales with session Mean.
///
/// Session Length mapping (1-10 → approximate real minutes):
///   1 → ~5 min, 5 → ~20 min, 10 → ~60 min.
///   The engine converts the user's 1-10 length into a total "budget" of seconds and
///   allocates them across the phases.
///
/// Active punishments are checked at plan generation time. If a CumMethod punishment
/// is active, the Finish step is overridden to use the prescribed method.
/// </summary>
public interface IInstructionEngine
{
    /// <summary>
    /// Generates a full session plan and persists the steps.
    /// Returns the ordered list of steps.
    /// </summary>
    Task<IReadOnlyList<InstructionStep>> GeneratePlanAsync(int sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a harsher punishment plan and persists the steps.
    /// Uses more torture methods, faster speeds, more edges, shorter rests.
    /// </summary>
    Task<IReadOnlyList<InstructionStep>> GeneratePunishmentPlanAsync(int sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the list of methods that are eligible for the given session
    /// (filtered by toy/kink flags and optionally capped by an intensity ceiling).
    /// </summary>
    Task<IReadOnlyList<StimMethod>> GetEligibleMethodsAsync(int sessionId, int? intensityCeiling = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates and resolves mini-game challenges during a session.
///
/// Design notes
/// ────────────
/// Games add unpredictability and gamification. They can be inserted by the
/// instruction engine at random points, or triggered when the user begs to cum.
///
/// <b>Number Guess</b>:
///   Domme picks a number 1-N (N scales with Mean). User guesses.
///   Wrong → punishment (tier based on how far off).
///   Right → reward (e.g. permission to cum, reduced edge count).
///   Can be used as a cum-permission gate: "Guess right and you can cum."
///
/// <b>Coin Flip</b>:
///   50/50 gamble with stakes announced up front.
///
/// <b>Edge Rush</b>:
///   "Edge N times in M minutes." Failure → punishment.
///
/// The probability of a game being inserted scales with session Mean and how
/// far into the session the user is (more likely near the peak phase).
/// </summary>
public interface IGameEngine
{
    /// <summary>
    /// Creates a new game challenge for the session and persists it.
    /// </summary>
    Task<GameChallenge> CreateChallengeAsync(int sessionId, GameType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits the user's answer and resolves the outcome.
    /// Returns the updated challenge with Won populated.
    /// </summary>
    Task<GameChallenge> SubmitAnswerAsync(int challengeId, int userAnswer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a number-guess challenge specifically for a cum request.
    /// The range and stakes scale with session parameters.
    /// </summary>
    Task<GameChallenge> CreateCumGuessChallengeAsync(int sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Real-time session engine that generates directives one at a time
/// based on session state, user interactions, and parameters.
///
/// Unlike <see cref="IInstructionEngine"/> (which pre-generates a plan of offline
/// homework tasks), this engine reacts to what the user does during a live session.
/// Each call to <see cref="CompleteStepAsync"/> or <see cref="RecordEdgeAsync"/>
/// may produce a new <see cref="SessionDirective"/> whose content depends on
/// the current phase, edges so far, session parameters, and randomness.
/// </summary>
public interface IOnlineSessionEngine
{
    /// <summary>
    /// Initialize the engine for a new live session.
    /// Returns the first directive (always a warm-up step).
    /// </summary>
    Task<SessionDirective> BeginSessionAsync(int sessionId, bool onlyGamesMode = false, CancellationToken ct = default);

    /// <summary>
    /// Initialize a punishment session — same engine but harsher directives,
    /// more torment, more edges, shorter rests, less mercy.
    /// </summary>
    Task<SessionDirective> BeginPunishmentSessionAsync(int sessionId, bool onlyGamesMode = false, CancellationToken ct = default);

    /// <summary>Snapshot of the engine's current state.</summary>
    OnlineSessionState GetCurrentState();

    /// <summary>
    /// Mark the current step as done and generate the next directive.
    /// Phase transitions happen automatically when time budget is exhausted.
    /// </summary>
    Task<SessionDirective> CompleteStepAsync(CancellationToken ct = default);

    /// <summary>
    /// Record an edge. If the current directive is an EdgeCount step and the
    /// target is now met, the engine auto-advances and returns the next directive.
    /// Returns <c>null</c> when the edge was counted but no step change occurred.
    /// </summary>
    Task<SessionDirective?> RecordEdgeAsync(CancellationToken ct = default);

    /// <summary>Immediately end the session (e.g. after cum permission granted).</summary>
    void EndSession();

    /// <summary>End the session and record how it ended.</summary>
    void EndSession(SessionOutcome outcome);

    /// <summary>
    /// Resolve an active game challenge. Cancels the step timer, adjusts
    /// internal state (points / retaliation), and returns the next directive.
    /// </summary>
    SessionDirective ResolveGame(bool won);

    /// <summary>
    /// Handle a reroll request during a RerollGamble game.
    /// Returns a new directive with a worse outcome, or null if max rerolls reached.
    /// </summary>
    SessionDirective? HandleReroll(int newOutcome);

    /// <summary>
    /// Notify the engine of an external event (e.g. beg denied) so it can
    /// adjust its internal mood / retaliation state.
    /// </summary>
    void NotifyEvent(SessionEvent evt);

    /// <summary>Whether a session is currently running in this engine.</summary>
    bool HasActiveSession { get; }

    /// <summary>
    /// Fired when a timed directive's duration expires and the engine auto-advances
    /// to the next step. The handler receives the new directive.
    /// </summary>
    event Action<SessionDirective>? StepTimerElapsed;
}
