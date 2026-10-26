using AIDomme.Sessions;

namespace AIDomme.Speech;

/// <summary>
/// Her two kinds of speech. Commands follow the plan: orders, reactions to what he does, the ending.
/// Chatter follows her persona: teasing, mind games, stories, remarks about what is on screen.
/// </summary>
public enum SpeechStream { Command, Chatter }

/// <summary>What she is saying, before it has words. The narrator turns an act into a line.</summary>
public enum SpeechAct
{
    // The session frame.
    SessionStart,
    Obligations,
    SessionEnd,

    // The plan.
    SegmentStart,
    Foreshadow,

    // Instructions: these become the command on screen.
    Instruct,
    Hold,
    Cooldown,
    Resume,
    Lube,
    FreePlay,
    Price,
    OfferDecision,
    Finish,
    Denied,
    PunishmentEnding,

    // Reactions to what he does.
    EdgeRemark,
    EdgeCounted,
    ChallengeFailed,
    ChallengeResult,
    DebtPaid,
    Blocked,
    BegCumDenied,
    BegRestGranted,
    BegRestDenied,
    AlreadyResting,
    Retaliation,
    OfferMade,
    OfferAccepted,
    OfferDeclined,
    OfferExpired,
    PriceProgress,
    PricePaid,
    MercyGranted,
    TimeUp,

    // Games.
    GamePrompt,
    GameProgress,
    GameResult,

    // Chatter.
    Chatter,
    Interject,
    Bridge,

    // Outside a session.
    PromiseAnswer,
    PromiseAccepted,
    PromiseDeclined,
    FollowUpAnswer,
}

/// <summary>
/// Something she should say, as data: the act, why (tags), the values a line may use (slots) and,
/// for instructions, the directive whose instruction is rendered exactly. Decision code produces
/// these and never text; only the narrator makes words.
/// </summary>
public sealed record Utterance
{
    public Utterance(SpeechAct act) => Act = act;

    public SpeechAct Act { get; init; }
    public SpeechStream Stream { get; init; } = SpeechStream.Command;
    /// <summary>For instruction acts: what to render exactly (method, pace, count, hold, time).</summary>
    public Directive? Directive { get; init; }
    /// <summary>Values a line may use by name, such as <c>count</c>, <c>days</c> or <c>offer</c>.</summary>
    public IReadOnlyDictionary<string, string> Slots { get; init; } = Empty;
    /// <summary>Why she says it (for example <c>requested</c>, <c>reason:begging</c>, <c>game:CoinFlip</c>).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
    /// <summary>The line becomes the command shown on screen.</summary>
    public bool Instruction { get; init; }

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public bool Has(string tag) => Tags.Contains(tag, StringComparer.OrdinalIgnoreCase);

    public static Utterance Of(SpeechAct act, params string[] tags) => new(act) { Tags = tags };

    public static Utterance Instruct(Directive d, SpeechAct act = SpeechAct.Instruct, params string[] tags)
        => new(act) { Directive = d, Instruction = true, Tags = tags };

    public Utterance With(string slot, object? value)
    {
        if (value is null) return this;
        var slots = new Dictionary<string, string>(Slots, StringComparer.OrdinalIgnoreCase) { [slot] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "" };
        return this with { Slots = slots };
    }

    public Utterance Tagged(params string[] tags) => this with { Tags = Tags.Concat(tags).ToArray() };
}
