using System;

namespace AIDome.Data;

/// <summary>The type of mini-game offered to the user.</summary>
public enum GameType
{
    /// <summary>"Pick a number 1-N; if you're wrong, punishment."</summary>
    NumberGuess = 0,
    /// <summary>Coin-flip style yes/no gamble.</summary>
    CoinFlip = 1,
    /// <summary>"How many edges can you do in N minutes?"</summary>
    EdgeRush = 2,
    /// <summary>Roll for ending type, can reroll but odds get worse.</summary>
    RerollGamble = 3,
    /// <summary>Avoid edge within a time limit under heavy stimulation.</summary>
    ArousalChallenge = 4,
    /// <summary>Remember and repeat a sequence of numbers.</summary>
    MemorySequence = 5,
    /// <summary>Solve a quick math problem under pressure.</summary>
    QuickMath = 6,
    /// <summary>Pick a card — low cards punish, high cards reward.</summary>
    CardDraw = 7,
    /// <summary>Count edges accurately while distracted — get the count wrong = punished.</summary>
    EdgeCounter = 8,
    /// <summary>Reach the edge within a time limit or face consequences.</summary>
    RaceToEdge = 9,
    /// <summary>Hold at the edge for N seconds without going over.</summary>
    EdgeEndurance = 10,
    /// <summary>Red-light/green-light — must stop/start within a time window.</summary>
    RedLightGreenLight = 11,
    /// <summary>Double-or-nothing: risk current reward for a bigger one.</summary>
    DoubleOrNothing = 12,
    /// <summary>Choose between two punishments — neither is good.</summary>
    PickYourPoison = 13,
}

/// <summary>
/// A mini-game challenge issued during a session. Outcomes feed the punishment
/// or reward pipeline.
/// </summary>
public sealed class GameChallenge : IUserOwned
{
    public int Id { get; set; }

    /// <summary>The Cove account that owns this row.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int UserId { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    public int SessionId { get; set; }
    public SessionRecord? Session { get; set; }

    public GameType GameType { get; set; }

    /// <summary>The correct answer (for NumberGuess, the secret number).</summary>
    public int CorrectAnswer { get; set; }

    /// <summary>The user's submitted answer (null = not answered yet).</summary>
    public int? UserAnswer { get; set; }

    /// <summary>Whether the user won or lost (null = pending).</summary>
    public bool? Won { get; set; }

    /// <summary>Human-readable prompt shown to the user.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What happens on failure (shown to user before they answer).</summary>
    public string FailureConsequence { get; set; } = string.Empty;

    /// <summary>What happens on success.</summary>
    public string SuccessReward { get; set; } = string.Empty;

    public DateTime? AnsweredAtUtc { get; set; }
}
