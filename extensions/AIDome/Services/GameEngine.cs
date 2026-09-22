using AIDome.Data;
using AIDome.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome.Services;

/// <summary>
/// Creates and resolves mini-game challenges during sessions.
/// Expanded game types with mood/meanness-aware stakes.
/// </summary>
public sealed class GameEngine : IGameEngine
{
    private readonly GuidedDataFactory _dbFactory;
    private readonly ILogger<GameEngine> _logger;

    public GameEngine(
        GuidedDataFactory dbFactory,
        ILogger<GameEngine> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<GameChallenge> CreateChallengeAsync(int sessionId, GameType type, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.OpenAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        int mean = Math.Clamp(session.Mean > 0 ? session.Mean : 5, 1, 10);
        int intensity = Math.Clamp(session.Intensity > 0 ? session.Intensity : 5, 1, 10);

        var challenge = type switch
        {
            GameType.NumberGuess => BuildNumberGuess(sessionId, mean),
            GameType.CoinFlip => BuildCoinFlip(sessionId, mean),
            GameType.EdgeRush => BuildEdgeRush(sessionId, intensity, mean),
            GameType.RerollGamble => BuildRerollGamble(sessionId, mean),
            GameType.ArousalChallenge => BuildArousalChallenge(sessionId, intensity, mean),
            GameType.MemorySequence => BuildMemorySequence(sessionId, mean),
            GameType.QuickMath => BuildQuickMath(sessionId, mean),
            GameType.CardDraw => BuildCardDraw(sessionId, mean),
            GameType.EdgeCounter => BuildEdgeCounter(sessionId, intensity, mean),
            GameType.RaceToEdge => BuildRaceToEdge(sessionId, intensity, mean),
            GameType.EdgeEndurance => BuildEdgeEndurance(sessionId, intensity, mean),
            GameType.RedLightGreenLight => BuildRedLightGreenLight(sessionId, intensity, mean),
            GameType.DoubleOrNothing => BuildDoubleOrNothing(sessionId, mean),
            GameType.PickYourPoison => BuildPickYourPoison(sessionId, intensity, mean),
            _ => BuildCoinFlip(sessionId, mean)
        };

        db.Add(challenge);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Game created: {Type} for session {SessionId} (secret={Secret})",
            type, sessionId, challenge.CorrectAnswer);
        return challenge;
    }

    public async Task<GameChallenge> SubmitAnswerAsync(int challengeId, int userAnswer, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.OpenAsync(ct);
        var challenge = await db.GameChallenges.FirstOrDefaultAsync(c => c.Id == challengeId, ct)
            ?? throw new InvalidOperationException($"Challenge {challengeId} not found");

        challenge.UserAnswer = userAnswer;
        challenge.AnsweredAtUtc = DateTime.UtcNow;

        challenge.Won = challenge.GameType switch
        {
            GameType.NumberGuess => userAnswer == challenge.CorrectAnswer,
            GameType.CoinFlip => userAnswer == challenge.CorrectAnswer,
            GameType.EdgeRush => userAnswer >= challenge.CorrectAnswer,
            GameType.RerollGamble => userAnswer == 0 && challenge.CorrectAnswer >= 4,
            GameType.ArousalChallenge => userAnswer == 0,
            GameType.MemorySequence => userAnswer == challenge.CorrectAnswer,
            GameType.QuickMath => userAnswer == challenge.CorrectAnswer,
            GameType.CardDraw => userAnswer == (challenge.CorrectAnswer >= 7 ? 1 : 0),
            GameType.EdgeCounter => userAnswer == challenge.CorrectAnswer,
            GameType.RaceToEdge => userAnswer == 1, // 1 = made it, 0 = failed
            GameType.EdgeEndurance => userAnswer == 1, // 1 = held it, 0 = went over
            GameType.RedLightGreenLight => userAnswer == 1, // 1 = obeyed, 0 = failed
            GameType.DoubleOrNothing => userAnswer == challenge.CorrectAnswer,
            GameType.PickYourPoison => true, // both options are bad; "winning" means you chose
            _ => userAnswer == challenge.CorrectAnswer
        };

        if (challenge.GameType == GameType.CardDraw)
        {
            string cardName = challenge.CorrectAnswer switch
            {
                1 => "Ace",
                11 => "Jack",
                12 => "Queen",
                13 => "King",
                _ => challenge.CorrectAnswer.ToString()
            };

            string revealed = $"The card was {cardName}.";
            challenge.SuccessReward = challenge.Won == true
                ? $"{revealed} You called it correctly. Reward earned."
                : challenge.SuccessReward;
            challenge.FailureConsequence = challenge.Won == true
                ? challenge.FailureConsequence
                : $"{revealed} Wrong call. Punishment incoming.";
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Game {Id} resolved: user={UserAnswer}, correct={Correct}, won={Won}",
            challengeId, userAnswer, challenge.CorrectAnswer, challenge.Won);
        return challenge;
    }

    public async Task<GameChallenge> CreateCumGuessChallengeAsync(int sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.OpenAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        int mean = Math.Clamp(session.Mean > 0 ? session.Mean : 5, 1, 10);
        int range = 4 + mean / 2;
        int secret = Random.Shared.Next(1, range + 1);

        var challenge = new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.NumberGuess,
            CorrectAnswer = secret,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Pick a number between 1 and {range}. Guess right and you might just get to cum. Guess wrong and you don't finish, plus you get punished.",
            FailureConsequence = "Denied + punishment tier based on how far off you were.",
            SuccessReward = "Permission to cum."
        };

        db.Add(challenge);
        await db.SaveChangesAsync(ct);
        return challenge;
    }

    // ──────────────────── Builders ────────────────────

    private static GameChallenge BuildNumberGuess(int sessionId, int mean)
    {
        int range = 4 + mean / 2;
        int secret = Random.Shared.Next(1, range + 1);
        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.NumberGuess,
            CorrectAnswer = secret,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} I'm thinking of a number between 1 and {range}. Guess correctly and you skip the next step. Get it wrong and you'll be punished.",
            FailureConsequence = "Wrong guess. Punishment scales with how badly you missed.",
            SuccessReward = "You skip the next step."
        };
    }

    private static GameChallenge BuildCoinFlip(int sessionId, int mean)
    {
        int secret = Random.Shared.Next(0, 2);
        // Stakes vary by meanness — meaner = both outcomes can be bad
        string stakes;
        if (mean >= 8)
        {
            var cruelStakes = new[]
            {
                "Heads means 3 minutes of palming. Tails means 10 extra edges. Either way, you lose.",
                "Win and you get 30 seconds of rest. Lose and you edge until I say stop.",
                "Right answer saves you from the worst. Wrong answer? You don't want to know.",
                "Heads means I double the speed. Tails means I double the time. Good luck.",
            };
            stakes = cruelStakes[Random.Shared.Next(cruelStakes.Length)];
        }
        else if (mean >= 5)
        {
            var midStakes = new[]
            {
                "If you're right, I'll speed things up for you. If you're wrong, that's 5 more edges right now.",
                "Get it right and you earn a short break. Wrong and the next step gets harder.",
                "Win and I'll go easier on the next one. Lose and I won't.",
                "Right means a reward. Wrong means punishment. Simple as that.",
            };
            stakes = midStakes[Random.Shared.Next(midStakes.Length)];
        }
        else
        {
            var niceStakes = new[]
            {
                "If you're right, you earn a short rest. If you're wrong, the next step lasts twice as long.",
                "Win and I'll let you take it slow. Lose and I pick the speed.",
                "Get it right and you earn points. Wrong and you just keep going as normal.",
            };
            stakes = niceStakes[Random.Shared.Next(niceStakes.Length)];
        }

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.CoinFlip,
            CorrectAnswer = secret,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} I'm flipping a coin. Call it — heads or tails? {stakes}",
            FailureConsequence = mean >= 7 ? "Harsh punishment. I told you the stakes." : "Penalty applied.",
            SuccessReward = mean >= 7 ? "You got lucky. Barely." : "Reward earned."
        };
    }

    private static GameChallenge BuildEdgeRush(int sessionId, int intensity, int mean)
    {
        int target = 2 + intensity / 2 + (mean >= 7 ? 1 : 0);
        int timeLimitSeconds = Math.Max(25, 115 - intensity * 7 - mean * 2);

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.EdgeRush,
            CorrectAnswer = target,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Let's see how fast you can go. I want {target} edges in {timeLimitSeconds} seconds. Hit the Edge button each time. When time is up, I'll make you report your count.",
            FailureConsequence = $"Punishment for failing. The farther short of {target} you fall, the harsher it should be.",
            SuccessReward = "Points earned. Well done."
        };
    }

    private static GameChallenge BuildRerollGamble(int sessionId, int mean)
    {
        int initialRoll = Random.Shared.Next(1, 6);

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.RerollGamble,
            CorrectAnswer = initialRoll,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} I rolled your current fate: {DescribeRerollOutcome(initialRoll)}. You can accept it, or reroll and let the house tighten the screws. There will be a cap on how many times I let you reroll.",
            FailureConsequence = "Whatever fate brings. It won't be good.",
            SuccessReward = DescribeRerollOutcome(initialRoll)
        };
    }

    private static GameChallenge BuildArousalChallenge(int sessionId, int intensity, int mean)
    {
        int timeLimitSeconds = 30 + (10 - mean) * 5;

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.ArousalChallenge,
            CorrectAnswer = 0,
            Prompt = $"{DommeText.Pick(DommeText.ArousalChallengeStart)} Stroke at high speed for {timeLimitSeconds} seconds and do not edge. If you hit the edge before time runs out, you fail immediately or I may make you finish the timer anyway.",
            FailureConsequence = "You edged. Points lost. Punishment added.",
            SuccessReward = $"You survived {timeLimitSeconds} seconds. Points earned."
        };
    }

    private static GameChallenge BuildMemorySequence(int sessionId, int mean)
    {
        // Generate a less-patterned sequence; meaner sessions use longer sequences.
        int length = 4 + mean / 2; // 4-9 digits
        int answer = 0;
        var digits = new List<int>();
        for (int i = 0; i < length; i++)
        {
            int d;
            do
            {
                d = Random.Shared.Next(0, 10);
            }
            while (digits.Count >= 2
                && digits[^1] == d
                && digits[^2] == d);

            if (digits.Count >= 2)
            {
                int a = digits[^2];
                int b = digits[^1];
                bool ascending = a + 1 == b && b + 1 == d;
                bool descending = a - 1 == b && b - 1 == d;
                if (ascending || descending)
                {
                    d = (d + Random.Shared.Next(2, 7)) % 10;
                }
            }

            digits.Add(d);
            answer = answer * 10 + d;
        }
        string sequence = string.Join(", ", digits);

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.MemorySequence,
            CorrectAnswer = answer,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Memorize this sequence: {sequence}. You only get a brief look. Keep stroking. When it disappears, type it back as one number.",
            FailureConsequence = "Wrong sequence. Your memory failed you. Punishment time.",
            SuccessReward = "Perfect recall. Points earned."
        };
    }

    private static GameChallenge BuildQuickMath(int sessionId, int mean)
    {
        // Math difficulty scales with mean.
        int a, b, answer;
        string problem;
        if (mean >= 8)
        {
            int c = Random.Shared.Next(2, 12);
            a = Random.Shared.Next(12, 40);
            b = Random.Shared.Next(7, 25);
            switch (Random.Shared.Next(3))
            {
                case 0:
                    answer = a * b;
                    problem = $"{a} times {b}";
                    break;
                case 1:
                    answer = a + b - c;
                    problem = $"{a} plus {b} minus {c}";
                    break;
                default:
                    answer = (a - c) * 2;
                    problem = $"({a} minus {c}) times 2";
                    break;
            }
        }
        else if (mean >= 5)
        {
            a = Random.Shared.Next(10, 40);
            b = Random.Shared.Next(4, 18);
            if (Random.Shared.Next(2) == 0)
            {
                answer = a + b;
                problem = $"{a} plus {b}";
            }
            else
            {
                answer = a - b;
                problem = $"{a} minus {b}";
            }
        }
        else
        {
            a = Random.Shared.Next(5, 25);
            b = Random.Shared.Next(3, 15);
            answer = a + b;
            problem = $"{a} plus {b}";
        }

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.QuickMath,
            CorrectAnswer = answer,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Quick math while you keep stroking. What's {problem}? Don't take too long.",
            FailureConsequence = "Wrong answer. Can't even do basic math while distracted? Extra edges for you.",
            SuccessReward = "Correct. At least your brain still works. Points earned."
        };
    }

    private static GameChallenge BuildCardDraw(int sessionId, int mean)
    {
        // Hidden card draw. User predicts whether the card will be low (1-6) or high (7-13).
        // Mean domme skews the deck against them.
        int card;
        if (mean >= 8)
            card = Random.Shared.Next(1, 10); // weighted toward low
        else if (mean >= 5)
            card = Random.Shared.Next(1, 14); // fair
        else
            card = Random.Shared.Next(4, 14); // weighted toward high

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.CardDraw,
            CorrectAnswer = card,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} I'm drawing a hidden card. Call it now: high card (7 or higher) or low card (6 or lower)?",
            FailureConsequence = "Wrong call. Punishment incoming.",
            SuccessReward = "Right call. Reward earned."
        };
    }

    private static GameChallenge BuildEdgeCounter(int sessionId, int intensity, int mean)
    {
        int target = 2 + intensity / 3; // 2-5

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.EdgeCounter,
            CorrectAnswer = target,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} I'm not telling you the number. Keep edging and logging each one until I stop you. Then tell me how many it was. If your count is wrong, you're punished.",
            FailureConsequence = "Wrong count. You lost track. That's extra punishment.",
            SuccessReward = "You kept the count straight. Points earned."
        };
    }

    private static GameChallenge BuildRaceToEdge(int sessionId, int intensity, int mean)
    {
        int timeLimit = 60 - mean * 3; // Mean=1→57s, Mean=10→30s
        timeLimit = Math.Max(20, timeLimit);

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.RaceToEdge,
            CorrectAnswer = 1,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Race. You have {timeLimit} seconds to reach the edge. Hit the Edge button the moment you get there. If time runs out first, you lose.",
            FailureConsequence = "Too slow. That's embarrassing. Punishment added.",
            SuccessReward = "You made it. Fast hands. Points earned."
        };
    }

    private static GameChallenge BuildEdgeEndurance(int sessionId, int intensity, int mean)
    {
        int holdSeconds = 10 + mean * 2; // Mean=1→12s, Mean=10→30s

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.EdgeEndurance,
            CorrectAnswer = 1,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Get to the edge first. Once you're there, signal it, and then hold it for {holdSeconds} seconds without going over. If you have to stop to avoid going over, that counts as failure.",
            FailureConsequence = "You couldn't hold it. Weak. Severe punishment.",
            SuccessReward = $"You held the edge for {holdSeconds} seconds. Impressive self-control. Points earned."
        };
    }

    private static GameChallenge BuildRedLightGreenLight(int sessionId, int intensity, int mean)
    {
        int rounds = 3 + mean / 3; // 3-6 rounds

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.RedLightGreenLight,
            CorrectAnswer = 1,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Red light, green light. Wait for my commands. We are doing {rounds} rounds, and I expect immediate obedience.",
            FailureConsequence = "You couldn't keep up. Disobedient hands deserve punishment.",
            SuccessReward = "Perfect obedience. That's what I like to see. Points earned."
        };
    }

    private static GameChallenge BuildDoubleOrNothing(int sessionId, int mean)
    {
        // Original stake: a small reward. User can risk it for double.
        int coin = Random.Shared.Next(0, 2);
        string baseReward = mean >= 6
            ? "30 seconds of rest"
            : "skipping the next step";

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.DoubleOrNothing,
            CorrectAnswer = coin,
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} You've earned {baseReward}. But I'm feeling generous. Double or nothing — call it, heads or tails. Win and you get double the reward. Lose and you get nothing plus a penalty.",
            FailureConsequence = "Nothing. You gambled and lost. Now you get punished for being greedy.",
            SuccessReward = $"Double reward. {baseReward}, twice over."
        };
    }

    private static GameChallenge BuildPickYourPoison(int sessionId, int intensity, int mean)
    {
        // Both options are bad — user picks the lesser evil
        var optionPairs = new (string opt1, string opt2)[]
        {
            ("3 minutes of palming at full speed", $"{3 + intensity / 2} edges with a 15-second hold on each"),
            ($"{60 + mean * 10} seconds of your worst method", $"{4 + mean / 2} edges back to back with no rest"),
            ("Slow torture strokes for 2 minutes at speed 2", $"{2 + intensity / 3} edges then hold the last one for 30 seconds"),
            ("Double the duration of the next step", "Add 5 extra edges to the session total"),
            ("Speed locked at maximum for the next 90 seconds", "3 minutes of the most intense method I have"),
        };

        var (opt1, opt2) = optionPairs[Random.Shared.Next(optionPairs.Length)];

        return new GameChallenge
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = sessionId,
            GameType = GameType.PickYourPoison,
            CorrectAnswer = 0, // doesn't matter — both lose
            Prompt = $"{DommeText.Pick(DommeText.GameIntros)} Pick your poison. Option 1: {opt1}. Option 2: {opt2}. Neither is pleasant. Choose wisely. Enter 1 or 2.",
            FailureConsequence = opt1,
            SuccessReward = opt2
        };
    }

    public static string DescribeRerollOutcome(int outcome) => outcome switch
    {
        1 => "Denied. No orgasm tonight. You walk away empty.",
        2 => "A ruined orgasm. You'll edge and let go. No touching as it happens.",
        3 => "A slow torture cum. Speed 1 only. It's going to take a while.",
        4 => "A normal orgasm. Full speed allowed. Lucky you.",
        5 => "An edge hold cum. Hold the edge for 20 seconds, then you can finish.",
        _ => "Denied."
    };
}
