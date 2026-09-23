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
