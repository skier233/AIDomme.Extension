using AIDome.Data;
using AIDome.Services;

namespace AIDome.Sessions;

public enum ConsequenceKind
{
    /// <summary>Hands off for Amount seconds.</summary>
    Rest,
    /// <summary>Amount edges, holding each for Hold seconds. Becomes torment when the user owns edges.</summary>
    ExtraEdges,
    /// <summary>Amount seconds of a torturous method.</summary>
    Torment,
    /// <summary>Amount points added to the session (negative lengthens it).</summary>
    Points,
    /// <summary>Amount percentage points on her next permission decision.</summary>
    PermissionOdds,
}

/// <summary>A game stake the domme applies once the game is over.</summary>
public sealed record Consequence(ConsequenceKind Kind, int Amount, int Hold = 0, string Description = "");

public enum GameInput { None, Choices, Number }

public sealed record GameChoice(string Label, int Value);

/// <summary>A running game. Plain data, saved with the session checkpoint.</summary>
public sealed class GameState
{
    public GameType Type { get; set; }
    /// <summary>The method stroking games use, fixed when the game starts.</summary>
    public MethodInfo? Method { get; set; }
    /// <summary>The audit row in aidomme_games, assigned by the host once it is written.</summary>
    public int RecordId { get; set; }
    public int Phase { get; set; }
    public int Secret { get; set; }
    public int Target { get; set; }
    public int Rounds { get; set; }
    public int Round { get; set; }
    public int Data { get; set; }
    public int EdgesAtPhaseStart { get; set; }
    public DateTime PhaseStartedAtUtc { get; set; }
    public GameInput Input { get; set; }
    public List<GameChoice> Choices { get; set; } = [];
    public string Prompt { get; set; } = "";
    public List<Consequence> Win { get; set; } = [];
    public List<Consequence> Loss { get; set; } = [];
    /// <summary>Per-choice stakes (pick-your-poison options, envelopes, reroll fates).</summary>
    public List<Consequence> Options { get; set; } = [];
}

/// <summary>What the game needs to know about the session.</summary>
public sealed record GameContext(int Mean, int Intensity, double Arousal, DommeMood Mood, MethodInfo StrokeMethod,
    bool DommeOwnsEdges, int EdgesThisStep, DateTime Now, IRandom Random);

/// <summary>
/// A game step: either the next instruction within the game, or the result. A null timer keeps the
/// one already running; zero means no timer.
/// </summary>
public sealed record GameStep(Directive? Next = null, int? TimerSeconds = 0, GameResult? Result = null)
{
    public static GameStep Continue(Directive next, int? timerSeconds = 0) => new(next, timerSeconds);
    public static GameStep Over(GameResult result) => new(Result: result);
}

/// <summary>The end of a game. Neutral results (a chosen poison) neither please nor anger her.</summary>
public sealed record GameResult(bool Won, string Line, IReadOnlyList<Consequence> Consequences, int? Answer = null, bool Neutral = false);

/// <summary>
/// The game catalogue. Every game either asks for a real decision or is judged from the tracker;
/// none asks the user to report something already on screen. Stakes are always applied.
/// </summary>
public static class Games
{
    /// <summary>Games that need the domme to set edge targets.</summary>
    private static readonly HashSet<GameType> EdgeGames = [GameType.RaceToEdge, GameType.EdgeEndurance, GameType.EdgeRush, GameType.EdgeWager];

    public static readonly GameType[] Playable =
    [
        GameType.CoinFlip, GameType.NumberGuess, GameType.CardDraw, GameType.DoubleOrNothing, GameType.PickYourPoison,
        GameType.RerollGamble, GameType.QuickMath, GameType.RaceToEdge, GameType.EdgeEndurance, GameType.ArousalChallenge,
        GameType.EdgeRush, GameType.RedLightGreenLight, GameType.Chicken, GameType.EdgeWager, GameType.TwoEnvelopes,
    ];

    /// <summary>Weighted choice of the next game, avoiding the last few played.</summary>
    public static GameType Choose(GameContext ctx, IReadOnlyList<GameType> recent, bool anyGame)
    {
        var pool = new List<(GameType, double)>();
        void Add(GameType type, double weight)
        {
            if (!ctx.DommeOwnsEdges && EdgeGames.Contains(type)) return;
            if (recent.TakeLast(3).Contains(type)) weight *= 0.05;
            pool.Add((type, weight));
        }
        if (anyGame)
        {
            foreach (var type in Playable) Add(type, 1);
            return ctx.Random.Weighted(pool, GameType.CoinFlip);
        }
        Add(GameType.CoinFlip, 8);
        Add(GameType.NumberGuess, 6);
        Add(GameType.CardDraw, 6);
        Add(GameType.DoubleOrNothing, 5);
        Add(GameType.TwoEnvelopes, 8);
        Add(GameType.QuickMath, 3);
        if (ctx.Arousal > 30) { Add(GameType.RaceToEdge, 6); Add(GameType.EdgeWager, 8); Add(GameType.Chicken, 8); }
        if (ctx.Arousal > 50) { Add(GameType.EdgeEndurance, 7); Add(GameType.RedLightGreenLight, 6); }
        if (ctx.Arousal > 60 && ctx.Mean >= 5) Add(GameType.ArousalChallenge, 9);
        if (ctx.Intensity >= 4) Add(GameType.EdgeRush, 6);
        if (ctx.Mean >= 6) Add(GameType.PickYourPoison, ctx.Mean >= 8 ? 12 : 6);
        if (ctx.Arousal > 70) Add(GameType.RerollGamble, 5);
        return ctx.Random.Weighted(pool, GameType.CoinFlip);
    }

    public static (GameState Game, GameStep First) Start(GameType type, GameContext ctx)
    {
        var r = ctx.Random;
        var intro = DommeText.Pick(DommeText.GameIntros);
        var game = new GameState { Type = type, PhaseStartedAtUtc = ctx.Now, Method = ctx.StrokeMethod };
        var heads = new List<GameChoice> { new("Heads", 0), new("Tails", 1) };
        switch (type)
        {
            case GameType.CoinFlip:
            {
                game.Secret = r.Next(0, 2);
                if (ctx.Mean >= 8) { game.Win = [Rest(30)]; game.Loss = [Edges(ctx, 4 + ctx.Mean / 3, 10)]; }
                else if (ctx.Mean >= 5) { game.Win = [Bonus(6)]; game.Loss = [Edges(ctx, 3, 0)]; }
                else { game.Win = [Rest(30), Bonus(4)]; game.Loss = [Torment(45)]; }
                game.Input = GameInput.Choices; game.Choices = heads;
                game.Prompt = $"{intro} I'm flipping a coin. Call it. Right, and {Describe(game.Win)}. Wrong, and {Describe(game.Loss)}.";
                return (game, Ask(game));
            }
            case GameType.NumberGuess:
            {
                int range = 4 + ctx.Mean / 2;
                game.Secret = r.Next(1, range + 1);
                game.Win = [Bonus(8), Rest(20)];
                game.Input = GameInput.Choices;
                game.Choices = Enumerable.Range(1, range).Select(x => new GameChoice(x.ToString(), x)).ToList();
                game.Prompt = $"{intro} I'm thinking of a number between 1 and {range}. Guess it and {Describe(game.Win)}. Miss, and you edge once for every step you were off.";
                return (game, Ask(game));
            }
            case GameType.CardDraw:
            {
                game.Secret = ctx.Mean >= 8 ? r.Next(1, 10) : ctx.Mean >= 5 ? r.Next(1, 14) : r.Next(4, 14);
                game.Win = [Bonus(6)];
                game.Loss = [Torment(30 + ctx.Mean * 5)];
                game.Input = GameInput.Choices; game.Choices = [new("High (7 or more)", 1), new("Low (6 or less)", 0)];
                game.Prompt = $"{intro} I'm drawing a card you can't see. High or low? Right, and {Describe(game.Win)}. Wrong, and {Describe(game.Loss)}.";
                return (game, Ask(game));
            }
            case GameType.DoubleOrNothing:
            {
                game.Secret = r.Next(0, 2);
                var stake = ctx.Mean >= 6 ? Rest(30) : Bonus(8);
                game.Options = [stake];
                game.Win = [stake with { Amount = stake.Amount * 2 }];
                game.Loss = [Torment(40 + ctx.Mean * 4)];
                game.Input = GameInput.Choices; game.Choices = [new("Take it", 2), new("Heads", 0), new("Tails", 1)];
                game.Prompt = $"{intro} You've earned {Describe([stake])}. Take it, or call a coin for double. Lose the call and you get nothing but {Describe(game.Loss)}.";
                return (game, Ask(game));
            }
            case GameType.PickYourPoison:
            {
                var pairs = new (Consequence, Consequence)[]
                {
                    (Torment(90 + ctx.Mean * 10), Edges(ctx, 3 + ctx.Intensity / 2, 15)),
                    (Torment(60 + ctx.Mean * 10), Edges(ctx, 4 + ctx.Mean / 2, 0)),
                    (Penalty(10 + ctx.Mean), Edges(ctx, 2 + ctx.Intensity / 3, 30)),
                    (Torment(120), Penalty(15)),
                };
                var (a, b) = r.Pick(pairs);
                game.Options = [a, b];
                game.Input = GameInput.Choices;
                game.Choices = [new($"1: {Describe([a])}", 1), new($"2: {Describe([b])}", 2)];
                game.Prompt = $"{intro} Pick your poison. One: {Describe([a])}. Two: {Describe([b])}. Neither is pleasant. Choose.";
                return (game, Ask(game));
            }
            case GameType.RerollGamble:
            {
                game.Options = Fates(ctx);
                game.Secret = r.Next(0, game.Options.Count);
                game.Target = 3 + r.Next(0, 3);
                game.Input = GameInput.Choices; game.Choices = [new("Accept", 0), new("Reroll", 1)];
                game.Prompt = $"{intro} Your fate: {game.Options[game.Secret].Description}. Accept it, or reroll and let the house tighten the screws. You get {game.Target} rerolls at most.";
                return (game, Ask(game));
            }
            case GameType.QuickMath:
            {
                var (problem, answer) = MathProblem(ctx);
                game.Secret = answer;
                game.Win = [Bonus(5)];
                game.Loss = [Edges(ctx, 2, 0)];
                game.Input = GameInput.Number;
                int seconds = Math.Max(8, ctx.Mean >= 8 ? 12 : 22 - ctx.Mean);
                game.Prompt = $"{intro} Keep stroking and tell me: what's {problem}? You have {seconds} seconds.";
                return (game, Ask(game, seconds));
            }
            case GameType.RaceToEdge:
            {
                int seconds = Math.Max(20, 60 - ctx.Mean * 3);
                game.Win = [Bonus(6), Rest(15)];
                game.Loss = [Torment(45 + ctx.Mean * 3)];
                game.Prompt = $"{intro} Race. {Stroke(ctx.StrokeMethod)}. Reach the edge within {seconds} seconds and record it. Too slow, and {Describe(game.Loss)}.";
                return (game, Play(game, ctx, 8, seconds));
            }
            case GameType.EdgeEndurance:
            {
                game.Target = 10 + ctx.Mean * 2;
                game.Win = [Bonus(game.Target / 3)];
                game.Loss = [Edges(ctx, 3, 15)];
                game.Prompt = $"{intro} Get to the edge. {Stroke(ctx.StrokeMethod)}. Record it when you're there, then hold it for {game.Target} seconds without going over. If you have to let go, admit it.";
                return (game, Play(game, ctx, 6, 0));
            }
            case GameType.ArousalChallenge:
            {
                int seconds = 30 + (10 - ctx.Mean) * 5;
                game.Target = seconds;
                game.Win = [Bonus(8)];
                game.Loss = [Edges(ctx, 3 + ctx.Mean / 3, 10)];
                game.Prompt = $"{DommeText.Pick(DommeText.ArousalChallengeStart)} {Stroke(ctx.StrokeMethod)}, fast, for {seconds} seconds, and do not edge. Edge before time is up and {Describe(game.Loss)}.";
                return (game, Play(game, ctx, 8, seconds));
            }
            case GameType.EdgeRush:
            {
                game.Target = 2 + ctx.Intensity / 2 + (ctx.Mean >= 7 ? 1 : 0);
                int seconds = Math.Max(30, 45 * game.Target - ctx.Mean * 3);
                game.EdgesAtPhaseStart = ctx.EdgesThisStep;
                game.Win = [Bonus(game.Target * 2), Rest(20)];
                game.Loss = [Torment(60 + ctx.Mean * 5)];
                game.Prompt = $"{intro} {game.Target} edges in {seconds} seconds. {Stroke(ctx.StrokeMethod)}. I'm counting every one you record.";
                return (game, Play(game, ctx, 8, seconds));
            }
            case GameType.RedLightGreenLight:
            {
                game.Rounds = 3 + ctx.Mean / 3;
                game.Win = [Bonus(8)];
                game.Loss = [Torment(60 + ctx.Mean * 5)];
                game.Prompt = $"{intro} Red light, green light. {game.Rounds} rounds. Stroke on green, hands off on red. An edge on red loses.";
                game.Phase = 1;
                return (game, GreenOrRed(game, ctx));
            }
            case GameType.Chicken:
            {
                game.Target = 60 + ctx.Mean * 3;
                game.Loss = [Edges(ctx, 3, 10), Penalty(10)];
                game.Input = GameInput.Choices; game.Choices = [new("Bail now", 1)];
                game.Prompt = $"{intro} Chicken. {Stroke(ctx.StrokeMethod)}, fast. Every ten seconds you last takes two points off your session. Bail whenever you like. Edge before you bail and you lose all of it, and more.";
                return (game, Play(game, ctx, 8, game.Target, answerReady: true));
            }
            case GameType.EdgeWager:
            {
                game.Input = GameInput.Choices;
                game.Choices = Enumerable.Range(1, 5).Select(x => new GameChoice($"Bet {x}", x)).ToList();
                game.Prompt = $"{intro} Place a bet: how many edges can you give me in {WagerSeconds(1)} to {WagerSeconds(5)} seconds? Every edge you bet pays three points if you make it. Miss, and you pay the same in torment.";
                return (game, Ask(game));
            }
            case GameType.TwoEnvelopes:
            {
                var mercy = new List<Consequence> { Rest(45), Bonus(8) };
                var cruelty = r.Next(0, 2) == 0 ? new List<Consequence> { Torment(90 + ctx.Mean * 5) } : [Edges(ctx, 5, 10)];
                game.Secret = r.Next(1, 3); // the envelope holding mercy
                game.Win = mercy; game.Loss = cruelty;
                double truthful = ctx.Mood switch { DommeMood.Cruel => 30, DommeMood.Playful or DommeMood.Teasing => 70, _ => 55 };
                int hinted = r.Chance(truthful) ? game.Secret : 3 - game.Secret;
                game.Input = GameInput.Choices; game.Choices = [new("Envelope 1", 1), new("Envelope 2", 2)];
                game.Prompt = $"{intro} Two envelopes. One holds mercy: {Describe(mercy)}. The other holds {Describe(cruelty)}. If I were you... I'd take envelope {hinted}. Whether I'm telling the truth is another matter.";
                return (game, Ask(game));
            }
            default:
                throw new ArgumentException($"{type} is not playable.");
        }
    }

    /// <summary>An edge while a game is running. Null means the game does not care about edges.</summary>
    public static GameStep? OnEdge(GameState game, GameContext ctx, int edgesThisStep)
    {
        switch (game.Type)
        {
            case GameType.RaceToEdge:
                return GameStep.Over(new(true, DommeText.Pick(["Fast hands. You made it.", "There it is. In time, barely.", "Good. Right on the line."]), game.Win));
            case GameType.EdgeEndurance when game.Phase == 0:
                game.Phase = 1;
                game.PhaseStartedAtUtc = ctx.Now;
                game.Input = GameInput.Choices; game.Choices = [new("I had to let go", 0)];
                return GameStep.Continue(Step(game, $"Now hold it right there for {game.Target} seconds. Don't you dare go over.", 0, StepCategory.Hold), game.Target);
            case GameType.ArousalChallenge:
                return GameStep.Over(new(false, DommeText.Pick(DommeText.ArousalChallengeLose), game.Loss));
            case GameType.EdgeRush:
            case GameType.EdgeWager when game.Phase == 1:
            {
                int made = edgesThisStep - game.EdgesAtPhaseStart;
                if (made < game.Target) return GameStep.Continue(Step(game, $"{made} of {game.Target}. Keep going.", 8, StepCategory.Game) with { Method = ctx.StrokeMethod }, null);
                return GameStep.Over(new(true, $"That's {game.Target}. You made it.", game.Win));
            }
            case GameType.RedLightGreenLight when game.Phase % 2 == 0:
                return GameStep.Over(new(false, "An edge on a red light. You couldn't stop when I told you to.", game.Loss));
            case GameType.Chicken:
                return GameStep.Over(new(false, "You edged before you bailed. Greedy. You lose all of it.", game.Loss));
            default:
                return null;
        }
    }

    /// <summary>The game's timer ran out.</summary>
    public static GameStep OnTimer(GameState game, GameContext ctx)
    {
        switch (game.Type)
        {
            case GameType.QuickMath:
                return GameStep.Over(new(false, "Too slow. You couldn't even manage that.", game.Loss));
            case GameType.RaceToEdge:
                return GameStep.Over(new(false, "Time's up. Too slow.", game.Loss));
            case GameType.EdgeEndurance:
                return GameStep.Over(new(true, $"{game.Target} seconds on the edge. Impressive self-control.", game.Win));
            case GameType.ArousalChallenge:
                return GameStep.Over(new(true, DommeText.Pick(DommeText.ArousalChallengeWin), game.Win));
            case GameType.EdgeRush:
            case GameType.EdgeWager:
                return GameStep.Over(new(false, $"Time's up. You didn't reach {game.Target}.", game.Loss));
            case GameType.RedLightGreenLight:
                if (game.Phase < game.Rounds * 2)
                {
                    game.Phase++;
                    return GreenOrRed(game, ctx);
                }
                game.Input = GameInput.Choices;
                game.Choices = [new("I obeyed every call", 1), new("I slipped", 0)];
                return GameStep.Continue(Step(game, $"All {game.Rounds} rounds done. Did you obey every call? Be honest.", 0, StepCategory.Game));
            case GameType.Chicken:
                return GameStep.Over(new(true, "You lasted the whole way without edging. I'm almost impressed.", [Bonus(game.Target / 5)]));
            default:
                return GameStep.Over(new(false, "Too late. You lose.", game.Loss));
        }
    }

    /// <summary>A submitted answer. Null when the answer is not valid right now.</summary>
    public static GameStep? OnAnswer(GameState game, int answer, GameContext ctx)
    {
        if (game.Input == GameInput.None) return null;
        if (game.Input == GameInput.Choices && game.Choices.All(x => x.Value != answer)) return null;
        switch (game.Type)
        {
            case GameType.CoinFlip:
            case GameType.CardDraw:
            {
                bool won = game.Type == GameType.CoinFlip ? answer == game.Secret : (game.Secret >= 7) == (answer == 1);
                string reveal = game.Type == GameType.CoinFlip ? (game.Secret == 0 ? "Heads." : "Tails.") : $"The card was {CardName(game.Secret)}.";
                return GameStep.Over(new(won, $"{reveal} {(won ? "Lucky you." : "Wrong.")}", won ? game.Win : game.Loss, answer));
            }
            case GameType.NumberGuess:
            {
                if (answer == game.Secret) return GameStep.Over(new(true, $"{game.Secret}. You got it.", game.Win, answer));
                int off = Math.Abs(answer - game.Secret);
                return GameStep.Over(new(false, $"It was {game.Secret}. You were {off} off.", [Edges(ctx, Math.Min(5, off), 0)], answer));
            }
            case GameType.DoubleOrNothing:
                if (answer == 2) return GameStep.Over(new(true, "Sensible. Take it.", game.Options, answer, Neutral: true));
                return answer == game.Secret
                    ? GameStep.Over(new(true, $"{(game.Secret == 0 ? "Heads" : "Tails")}. Double it is.", game.Win, answer))
                    : GameStep.Over(new(false, $"{(game.Secret == 0 ? "Heads" : "Tails")}. Greedy, and wrong.", game.Loss, answer));
            case GameType.PickYourPoison:
                return GameStep.Over(new(false, $"Option {answer}. Your choice. Live with it.", [game.Options[answer - 1]], answer, Neutral: true));
            case GameType.RerollGamble:
            {
                if (answer == 0 || game.Data >= game.Target)
                {
                    var fate = game.Options[game.Secret];
                    string line = answer == 0 ? $"Accepted: {fate.Description}." : $"No more rerolls. The house wins: {fate.Description}.";
                    return GameStep.Over(new(game.Secret >= game.Options.Count / 2, line, [fate], answer, Neutral: true));
                }
                game.Data++;
                // Each reroll leans a little further toward the worse fates at the front of the list.
                int roll = ctx.Random.Next(0, game.Options.Count);
                game.Secret = Math.Max(0, roll - (game.Data - 1));
                var text = $"Reroll {game.Data}. New fate: {game.Options[game.Secret].Description}. Accept, or push your luck?";
                if (game.Data >= game.Target) { game.Choices = [new("Accept", 0)]; text = $"Reroll {game.Data}, your last. Your fate: {game.Options[game.Secret].Description}."; }
                return GameStep.Continue(Step(game, text, 0, StepCategory.Game));
            }
            case GameType.QuickMath:
                return answer == game.Secret
                    ? GameStep.Over(new(true, "Correct. At least your brain still works.", game.Win, answer))
                    : GameStep.Over(new(false, $"Wrong. It was {game.Secret}.", game.Loss, answer));
            case GameType.EdgeEndurance:
                return GameStep.Over(new(false, "You let go. Weak.", game.Loss, answer));
            case GameType.RedLightGreenLight:
                return answer == 1
                    ? GameStep.Over(new(true, "Good. That's what obedience looks like.", game.Win, answer))
                    : GameStep.Over(new(false, "At least you're honest. You still pay.", game.Loss, answer));
            case GameType.Chicken:
            {
                int seconds = (int)Math.Max(0, (ctx.Now - game.PhaseStartedAtUtc).TotalSeconds);
                int points = seconds / 10 * 2;
                return GameStep.Over(new(points > 0, points > 0 ? $"You bailed after {seconds} seconds. That's {points} points off." : "Bailed already? Coward. Nothing for you.",
                    points > 0 ? [Bonus(points)] : [], answer));
            }
            case GameType.EdgeWager when game.Phase == 0:
            {
                game.Target = answer;
                game.Phase = 1;
                game.EdgesAtPhaseStart = ctx.EdgesThisStep;
                game.Win = [Bonus(answer * 3)];
                game.Loss = [Torment(20 * answer + ctx.Mean * 3)];
                game.Input = GameInput.None; game.Choices = [];
                int seconds = WagerSeconds(answer);
                return GameStep.Continue(Step(game, $"You bet {answer}. {Stroke(ctx.StrokeMethod)}. {seconds} seconds, starting now.", 8, StepCategory.Game) with { Method = ctx.StrokeMethod }, seconds);
            }
            case GameType.TwoEnvelopes:
            {
                bool won = answer == game.Secret;
                return GameStep.Over(new(won, won ? "Mercy. You chose well... this time." : "Oh dear. That was the other one.", won ? game.Win : game.Loss, answer));
            }
            default:
                return null;
        }
    }

    /// <summary>Plain-language description of stakes, used in prompts and choice labels.</summary>
    public static string Describe(IReadOnlyList<Consequence> consequences)
        => consequences.Count == 0 ? "nothing" : string.Join(" and ", consequences.Select(Describe));

    private static string Describe(Consequence c) => c.Description != "" ? c.Description : c.Kind switch
    {
        ConsequenceKind.Rest => $"{c.Amount} seconds of rest",
        ConsequenceKind.ExtraEdges => c.Hold > 0 ? $"{c.Amount} extra edges held for {c.Hold} seconds each" : $"{c.Amount} extra edges",
        ConsequenceKind.Torment => $"{c.Amount} seconds of torment",
        ConsequenceKind.Points => c.Amount >= 0 ? $"{c.Amount} points off your session" : $"{-c.Amount} points added to your session",
        ConsequenceKind.PermissionOdds => c.Amount >= 0 ? "better odds when I decide your ending" : "worse odds when I decide your ending",
        _ => "",
    };

    private static Consequence Rest(int seconds) => new(ConsequenceKind.Rest, seconds);
    private static Consequence Torment(int seconds) => new(ConsequenceKind.Torment, seconds);
    private static Consequence Bonus(int points) => new(ConsequenceKind.Points, points);
    private static Consequence Penalty(int points) => new(ConsequenceKind.Points, -points);
    private static Consequence Edges(GameContext ctx, int count, int hold)
        => ctx.DommeOwnsEdges ? new(ConsequenceKind.ExtraEdges, count, hold) : new(ConsequenceKind.Torment, count * 20);

    private static List<Consequence> Fates(GameContext ctx) =>
    [
        new(ConsequenceKind.PermissionOdds, -40, 0, "denial becomes very likely tonight"),
        new(ConsequenceKind.Torment, 120, 0, "two minutes of your worst"),
        ctx.DommeOwnsEdges ? new(ConsequenceKind.ExtraEdges, 5, 20, "five edges, each held for twenty seconds") : new(ConsequenceKind.Torment, 90, 0, "ninety seconds of torment"),
        new(ConsequenceKind.Points, 10, 0, "a little mercy: ten points off your session"),
        new(ConsequenceKind.PermissionOdds, 20, 0, "I'll be generous when I decide your ending"),
    ];

    private static int WagerSeconds(int bet) => 30 + bet * 25;

    private static string CardName(int card) => card switch { 1 => "an ace", 11 => "a jack", 12 => "a queen", 13 => "a king", _ => $"a {card}" };

    private static (string, int) MathProblem(GameContext ctx)
    {
        var r = ctx.Random;
        if (ctx.Mean >= 8)
        {
            int a = r.Next(12, 40), b = r.Next(7, 25), c = r.Next(2, 12);
            return r.Next(0, 3) switch
            {
                0 => ($"{a} times {b}", a * b),
                1 => ($"{a} plus {b} minus {c}", a + b - c),
                _ => ($"{a} minus {c}, times 2", (a - c) * 2),
            };
        }
        if (ctx.Mean >= 5)
        {
            int a = r.Next(10, 40), b = r.Next(4, 18);
            return r.Next(0, 2) == 0 ? ($"{a} plus {b}", a + b) : ($"{a} minus {b}", a - b);
        }
        int x = r.Next(5, 25), y = r.Next(3, 15);
        return ($"{x} plus {y}", x + y);
    }

    /// <summary>"Use {method}" for a prescribed method; the user's own choice otherwise.</summary>
    public static string Stroke(MethodInfo method)
        => method.IsUserChoice ? "Stroke however you like" : method.Description.TrimEnd('.', ' ') is { Length: > 0 } d ? d : method.Name;

    private static GameStep Ask(GameState game, int timerSeconds = 0)
        => GameStep.Continue(Step(game, game.Prompt, 0, StepCategory.Game), timerSeconds);

    private static GameStep Play(GameState game, GameContext ctx, int speed, int timerSeconds, bool answerReady = false)
    {
        if (!answerReady) { game.Input = GameInput.None; game.Choices = []; }
        return GameStep.Continue(Step(game, game.Prompt, speed, StepCategory.Game) with { Method = ctx.StrokeMethod }, timerSeconds);
    }

    private static GameStep GreenOrRed(GameState game, GameContext ctx)
    {
        bool green = game.Phase % 2 == 1;
        int seconds = green ? 5 + ctx.Random.Next(0, 10) : 3 + ctx.Random.Next(0, 6);
        string text = green ? "GREEN LIGHT. Stroke, fast." : "RED LIGHT. Hands off. Now.";
        if (game.Phase == 1) text = $"{game.Prompt} {text}";
        return GameStep.Continue(Step(game, text, green ? 8 : 0, StepCategory.Game) with { Method = green ? ctx.StrokeMethod : null }, seconds);
    }

    private static Directive Step(GameState game, string text, int speed, StepCategory category) => new()
    {
        Kind = InstructionKind.FreeText, Category = category, Speed = speed, Text = text, Phase = SessionPhase.Intense,
    };
}
