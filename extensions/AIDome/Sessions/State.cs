using System.Text.Json.Serialization;
using AIDome.Data;
using AIDome.Sessions.Plan;
using AIDome.Speech;

namespace AIDome.Sessions;

/// <summary>The domme's current temperament; it colors what she chooses and how she says it.</summary>
public enum DommeMood { Neutral, Cruel, Edgy, Teasing, Playful, Relentless }

/// <summary>What kind of instruction a step is, from the domme's point of view.</summary>
public enum StepCategory { Tease, Stroke, Edge, Rest, Torment, Surprise, Game, Psych, Hold, Finish, FreePlay, Offer }

/// <summary>A label for what the session is doing right now.</summary>
public enum SessionPhase { Teasing = 0, Building = 1, Intense = 2, Edging = 3, Resting = 4, Tormenting = 5, Finishing = 6, Ended = 7 }

/// <summary>Why the domme is being harsher, so her words match.</summary>
public enum RetaliationReason { BegCumDenied = 0, BegStopDenied = 1, Disobedience = 2, UnauthorizedOrgasm = 3, RuinedOrgasm = 4, LostGame = 5, FailedChallenge = 6 }

/// <summary>Why her mood is what it is, so she can say so.</summary>
public enum MoodCause
{
    None, Temperament, LastSession, Begging, Pestering, Obedience, Disobedience, UnauthorizedOrgasm, FailedChallenge,
    LostGame, WonGame, Deal, BrokenPromise, Drift,
}

/// <summary>What the user should be doing right now.</summary>
public sealed record Directive
{
    public InstructionKind Kind { get; init; }
    public StepCategory Category { get; init; }
    /// <summary>The prescribed method; the user-choice placeholder when the user owns methods; null for rests.</summary>
    public MethodInfo? Method { get; init; }
    /// <summary>1–10 when the domme sets the pace, otherwise 0.</summary>
    public int Speed { get; init; }
    /// <summary>Metronome beats per minute; 0 means silence (rest, hold, stop, or user-set pace).</summary>
    public int Tempo { get; init; }
    /// <summary>Seconds for timed steps; edges for edge-count steps.</summary>
    public int DurationOrCount { get; init; }
    public int HoldSeconds { get; init; }
    /// <summary>Her words for this instruction, filled in by the narrator; the engine never writes it.</summary>
    public string Text { get; init; } = "";
    public SessionPhase Phase { get; init; }
    /// <summary>A timer the user does not see counting down (free-play check-ins).</summary>
    public bool HiddenTimer { get; init; }
    /// <summary>The segment type this step belongs to, if the session has a plan.</summary>
    public string? Segment { get; init; }
    /// <summary>A challenge: an edge before the time is up fails it.</summary>
    public bool ForbidEdge { get; init; }
    /// <summary>A challenge: every edge in the time counts, and none interrupts the step.</summary>
    public bool CountEdges { get; init; }
}

/// <summary>A punishment ending from the library, snapshotted for a session that owes one.</summary>
public sealed record EnderInfo
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Announcement { get; init; } = "";
    public string Instruction { get; init; } = "";
    public int Severity { get; init; }
    public bool EndsInOrgasm { get; init; } = true;

    public static EnderInfo From(PunishmentEnder e) => new()
    {
        Id = e.Id, Name = e.Name, Announcement = e.AnnouncementText, Instruction = e.InstructionText, Severity = e.Severity,
        EndsInOrgasm = e.EndsInOrgasm,
    };
}

public sealed class MoodState
{
    public DommeMood Mood { get; set; }
    public int StepsRemaining { get; set; }
    public double Intensity { get; set; }
    /// <summary>What put her in this mood.</summary>
    public MoodCause Cause { get; set; }
    /// <summary>0 (pleased with him) to 1 (angry). Rises when he begs or disobeys, falls when he obeys.</summary>
    public double Annoyance { get; set; }
}

public sealed class RetaliationState
{
    public bool Active { get; set; }
    public int StepsLeft { get; set; }
    public RetaliationReason Reason { get; set; }
}

/// <summary>
/// Everything the engine knows about one running session. It is plain data: the checkpoint is
/// this object serialized, so recovery restores exactly what the engine was using.
/// </summary>
public sealed class SessionState
{
    public int Format { get; set; } = 2;
    public int SessionId { get; set; }
    public SessionContract Contract { get; set; } = new();
    public int ContractRevision { get; set; } = 1;
    /// <summary>The account's effective method library at start, within the contract's limits.</summary>
    public List<MethodInfo> Methods { get; set; } = [];
    /// <summary>What each Length means to him, in minutes; with his pace it sets the points budget.</summary>
    public SessionLengths Lengths { get; set; } = new();
    /// <summary>How he wants sessions to end, in general (the session's Denial and limits apply on top).</summary>
    public EndingPreferences Endings { get; set; } = new();

    /// <summary>Whether she may ruin an orgasm tonight at all: his general preference, then this session's limit.</summary>
    [JsonIgnore] public bool RuinsAllowed => Endings.Ruins != RuinPreference.Never && !Contract.Limits.NoRuin;

    public DateTime StartedAtUtc { get; set; }
    public bool Ended { get; set; }
    public SessionOutcome Outcome { get; set; }
    public bool Paused { get; set; }
    public DateTime? PausedAtUtc { get; set; }
    public double PausedSeconds { get; set; }

    public Directive? Current { get; set; }
    public int StepNumber { get; set; }
    public DateTime StepStartedAtUtc { get; set; }
    public int EdgesThisStep { get; set; }
    public int EdgesThisSession { get; set; }
    public int RequestsThisSession { get; set; }
    public int UnauthorizedOrgasms { get; set; }
    /// <summary>Hours since the user's last recorded orgasm when the session began; null if never recorded.</summary>
    public double? HoursSinceLastOrgasmAtStart { get; set; }
    /// <summary>The last thing the user did (edge, request, answer, resume). Unreported free play stops counting after a while.</summary>
    public DateTime LastActivityUtc { get; set; }

    // Timer: the host arms one timer for TimerDueUtc; a stale generation is ignored.
    public long TimerGeneration { get; set; }
    public DateTime? TimerDueUtc { get; set; }
    public int PausedTimerSeconds { get; set; }

    // Points are the session clock: the same activity earns the same points in every mode.
    public double Points { get; set; }
    public double Budget { get; set; }
    /// <summary>The minutes his Length means (a punishment runs longer), and the pace she assumed going in: the budget follows his pace tonight.</summary>
    public double BudgetMinutes { get; set; }
    public double AssumedPace { get; set; }
    /// <summary>Start of stimulation not yet converted into points (null while resting or paused).</summary>
    public DateTime? ActiveSinceUtc { get; set; }
    public MethodInfo? ActiveMethod { get; set; }
    public int ActiveSpeed { get; set; }

    /// <summary>Where the points came from, so a session has a profile as well as a total.</summary>
    public PointsEarned Earned { get; set; } = new();
    /// <summary>His state as she estimates it: closeness, need, control, overwhelm.</summary>
    public BodyState Body { get; set; } = new();
    /// <summary>What she believes about his body: pace, recovery, tolerance.</summary>
    public ProfileState Profile { get; set; } = new();
    /// <summary>Her estimate of how close he is, 0 to 100.</summary>
    [JsonIgnore] public double Arousal => Body.Closeness * 100;
    public int StepsSinceRest { get; set; }
    public List<int> RecentIntensities { get; set; } = [];
    public List<int> RecentMethodIds { get; set; } = [];
    public bool LubeActive { get; set; }
    public Directive? PendingLube { get; set; }
    /// <summary>A timed step interrupted by an edge, resumed after the hold or cooldown.</summary>
    public Directive? Suspended { get; set; }
    public int SuspendedRemainingSeconds { get; set; }
    public bool Holding { get; set; }
    public StepCategory? PreviousCategory { get; set; }
    public int? ChainMethodId { get; set; }
    public int ChainStepsRemaining { get; set; }
    public MoodState Mood { get; set; } = new();
    public RetaliationState Retaliation { get; set; } = new();

    public GameState? Game { get; set; }
    public List<GameType> RecentGames { get; set; } = [];
    /// <summary>Game stakes the domme still has to apply, in order.</summary>
    public List<Consequence> PendingConsequences { get; set; } = [];
    /// <summary>Percentage points added to (or taken from) her next permission decision.</summary>
    public double PermissionModifier { get; set; }

    /// <summary>The domme has decided the ending (or granted a request); no further steps are planned.</summary>
    public bool FinishDecided { get; set; }
    public bool PermissionGranted { get; set; }
    /// <summary>The permitted finish is ruined; <see cref="FinishRuinOwed"/> when that serves an owed ruin.</summary>
    public bool FinishRuined { get; set; }
    public bool FinishRuinOwed { get; set; }
    /// <summary>The finish is an owed punishment ending.</summary>
    public int? FinishEnderId { get; set; }

    // Obligations: what the user owes, from the ledger and commitments in force, and what was served.
    /// <summary>No orgasm before this time (<see cref="DateTime.MaxValue"/>: the whole session).</summary>
    public DateTime? DenialUntilUtc { get; set; }
    public string? DenialReason { get; set; }
    public int OwedEdges { get; set; }
    public int EdgesPaid { get; set; }
    public bool OwedEdgesAnnounced { get; set; }
    public int OwedRuins { get; set; }
    public int RuinsServed { get; set; }
    public int OwedEndings { get; set; }
    public int EndingsServed { get; set; }
    public List<EnderInfo> Enders { get; set; } = [];

    // Deals.
    public List<OfferTemplate> OfferTemplates { get; set; } = [];
    public List<int> RecentOfferTemplates { get; set; } = [];
    /// <summary>Offers he turned down or let lapse this session: each makes her next price higher.</summary>
    public List<RefusedOffer> RefusedOffers { get; set; } = [];
    public PendingOffer? Offer { get; set; }
    public Price? Price { get; set; }

    /// <summary>Set when the session stopped without her or the user pausing it (Cove closed, nobody there).</summary>
    public DateTime? InterruptedAtUtc { get; set; }

    // What she is doing and why: the plan (null in free play), the ending she leans toward, and what
    // she remembers of him from earlier sessions.
    public SessionPlan? Plan { get; set; }
    public EndingLean Ending { get; set; } = new();
    public Recall Recall { get; set; } = new();
    /// <summary>What she has said: lines used this session, a story in progress, when she last spoke.</summary>
    public SpeechMemory Speech { get; set; } = new();

    // What he has done this session, for her decisions and her words.
    public int RestRequests { get; set; }
    public int RequestsDenied { get; set; }
    public int ChallengesFailed { get; set; }
    public int ChallengesPassed { get; set; }
    public int GamesWon { get; set; }
    public int GamesLost { get; set; }
    public int OffersAccepted { get; set; }
    public int OffersDeclined { get; set; }

    [JsonIgnore] public int Mean => Pref(Contract.Preferences.Mean);
    [JsonIgnore] public int Intensity => Pref(Contract.Preferences.Intensity);
    [JsonIgnore] public int Denial => Pref(Contract.Preferences.Denial);
    [JsonIgnore] public int Length => Pref(Contract.Preferences.Length);
    [JsonIgnore] public SessionPersona Persona => Contract.Persona;
    [JsonIgnore] public double Progress => Budget <= 0 ? 0 : Points / Budget;
    [JsonIgnore] public bool Active => SessionId > 0 && !Ended;
    private static int Pref(int value) => value is >= 1 and <= 10 ? value : 5;
}

/// <summary>An offer he refused, remembered so the next one for the same thing costs more.</summary>
public sealed record RefusedOffer(OfferBuys Buys, int TemplateId, double Points);

/// <summary>
/// What earned the session's points, counted separately: plain stimulation, what being close added
/// to it, torturous stimulation, edges and holds. Whatever is left of the total came from games and deals.
/// </summary>
public sealed class PointsEarned
{
    public double Stroking { get; set; }
    /// <summary>What his closeness added to (or, far from the edge, took from) the stimulation.</summary>
    public double Close { get; set; }
    public double Torture { get; set; }
    public double Edges { get; set; }
    public double Holds { get; set; }
}
