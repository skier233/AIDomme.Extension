using AIDomme.Data;
using AIDomme.Speech;

namespace AIDomme.Sessions;

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
    /// <summary>Her words when the game started, as narrated (kept for the audit row).</summary>
    public string Prompt { get; set; } = "";
    public List<Consequence> Win { get; set; } = [];
    public List<Consequence> Loss { get; set; } = [];
    /// <summary>Per-choice stakes (pick-your-poison options, envelopes, reroll fates).</summary>
    public List<Consequence> Options { get; set; } = [];
}

/// <summary>What the game needs to know about the session.</summary>
public sealed record GameContext(int Mean, int Intensity, double Arousal, DommeMood Mood, MethodInfo StrokeMethod,
    bool DommeOwnsEdges, int EdgesThisStep, DateTime Now, IRandom Random)
{
    /// <summary>What she is pursuing when the game starts: its stakes are in that currency.</summary>
    public Plan.Purpose? Aim { get; init; }
    /// <summary>Roughly how long he takes to reach the edge from where he is, stroking fast: games that race him to it are sized by this.</summary>
    public double EdgeSeconds { get; init; } = 45;
}

/// <summary>
/// A game step: either the next instruction within the game, or the result. A null timer keeps the
/// one already running; zero means no timer.
/// </summary>
public sealed record GameStep(Directive? Next = null, int? TimerSeconds = 0, GameResult? Result = null, Utterance? Say = null)
{
    public static GameStep Continue(Directive next, Utterance say, int? timerSeconds = 0) => new(next, timerSeconds, Say: say);
    public static GameStep Over(GameResult result) => new(Result: result);
}

/// <summary>The end of a game, and what she says about it. Neutral results (a chosen poison) neither please nor anger her.</summary>
public sealed record GameResult(bool Won, Utterance Say, IReadOnlyList<Consequence> Consequences, int? Answer = null, bool Neutral = false);

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
        var game = new GameState { Type = type, PhaseStartedAtUtc = ctx.Now, Method = ctx.StrokeMethod };
        var heads = new List<GameChoice> { new("Heads", 0), new("Tails", 1) };
        switch (type)
        {
            case GameType.CoinFlip:
            {
                game.Secret = r.Next(0, 2);
                if (ctx.Mean >= 8) { game.Win = [Rest(30)]; game.Loss = [Edges(ctx, 4 + ctx.Mean / 3, 10)]; }
                else if (ctx.Mean >= 5) { game.Win = [Bonus(6)]; game.Loss = [Edges(ctx, 3, 0)]; }
                else { game.Win = [Rest(30), Bonus(4)]; game.Loss = [Torment(ctx, 45)]; }
                game.Input = GameInput.Choices; game.Choices = heads;
                return (game, Ask(game, Say(game, "prompt").With("win", Describe(game.Win)).With("loss", Describe(game.Loss))));
            }
            case GameType.NumberGuess:
            {
                int range = 4 + ctx.Mean / 2;
                game.Secret = r.Next(1, range + 1);
                game.Win = [Bonus(8), Rest(20)];
                game.Input = GameInput.Choices;
                game.Choices = Enumerable.Range(1, range).Select(x => new GameChoice(x.ToString(), x)).ToList();
                return (game, Ask(game, Say(game, "prompt").With("target", range).With("win", Describe(game.Win))
                    .Tagged(ctx.DommeOwnsEdges ? "edges" : "torment")));
            }
            case GameType.CardDraw:
            {
                game.Secret = ctx.Mean >= 8 ? r.Next(1, 10) : ctx.Mean >= 5 ? r.Next(1, 14) : r.Next(4, 14);
                game.Win = [Bonus(6)];
                game.Loss = [Torment(ctx, 30 + ctx.Mean * 5)];
                game.Input = GameInput.Choices; game.Choices = [new("High (7 or more)", 1), new("Low (6 or less)", 0)];
                return (game, Ask(game, Say(game, "prompt").With("win", Describe(game.Win)).With("loss", Describe(game.Loss))));
            }
            case GameType.DoubleOrNothing:
            {
                game.Secret = r.Next(0, 2);
                var stake = ctx.Mean >= 6 ? Rest(30) : Bonus(8);
                game.Options = [stake];
                game.Win = [stake with { Amount = stake.Amount * 2 }];
                game.Loss = [Torment(ctx, 40 + ctx.Mean * 4)];
                game.Input = GameInput.Choices; game.Choices = [new("Take it", 2), new("Heads", 0), new("Tails", 1)];
                return (game, Ask(game, Say(game, "prompt").With("win", Describe([stake])).With("loss", Describe(game.Loss))));
            }
            case GameType.PickYourPoison:
            {
                var pairs = new (Consequence, Consequence)[]
                {
                    (Torment(ctx, 90 + ctx.Mean * 10), Edges(ctx, 3 + ctx.Intensity / 2, 15)),
                    (Torment(ctx, 60 + ctx.Mean * 10), Edges(ctx, 4 + ctx.Mean / 2, 0)),
                    (Penalty(10 + ctx.Mean), Edges(ctx, 2 + ctx.Intensity / 3, 30)),
                    (Torment(ctx, 120), Penalty(15)),
                };
                var (a, b) = r.Pick(pairs);
                game.Options = [a, b];
                game.Input = GameInput.Choices;
                game.Choices = [new($"1: {Describe([a])}", 1), new($"2: {Describe([b])}", 2)];
                return (game, Ask(game, Say(game, "prompt").With("win", Describe([a])).With("loss", Describe([b]))));
            }
            case GameType.RerollGamble:
            {
                game.Options = Fates(ctx);
                game.Secret = r.Next(0, game.Options.Count);
                game.Target = 3 + r.Next(0, 3);
                game.Input = GameInput.Choices; game.Choices = [new("Accept", 0), new("Reroll", 1)];
                return (game, Ask(game, Say(game, "prompt").With("fate", game.Options[game.Secret].Description).With("target", game.Target)));
            }
            case GameType.QuickMath:
            {
                var (problem, answer) = MathProblem(ctx);
                game.Secret = answer;
                game.Win = [Bonus(5)];
                game.Loss = [Edges(ctx, 2, 0)];
                game.Input = GameInput.Number;
                int seconds = Math.Max(8, ctx.Mean >= 8 ? 12 : 22 - ctx.Mean);
                return (game, Ask(game, Say(game, "prompt").With("rules", problem).With("seconds", seconds), seconds));
            }
            case GameType.RaceToEdge:
            {
                // Sized to him: a little longer than he should need, less the meaner she is.
                int seconds = (int)Math.Clamp(ctx.EdgeSeconds * (1.3 - ctx.Mean * 0.04), 15, 150);
                game.Win = [Bonus(6), Rest(15)];
                game.Loss = [Torment(ctx, 45 + ctx.Mean * 3)];
                return (game, Play(game, ctx, 8, seconds, Say(game, "prompt").With("seconds", seconds).With("loss", Describe(game.Loss))));
            }
            case GameType.EdgeEndurance:
            {
                game.Target = 10 + ctx.Mean * 2;
                game.Win = [Bonus(game.Target / 3)];
                game.Loss = [Edges(ctx, 3, 15)];
                return (game, Play(game, ctx, 6, 0, Say(game, "prompt").With("target", game.Target)));
            }
            case GameType.ArousalChallenge:
            {
                // About as long as it takes him to get there: holding back is the whole game.
                int seconds = (int)Math.Clamp(ctx.EdgeSeconds * (0.7 + ctx.Mean * 0.04), 20, 120);
                game.Target = seconds;
                game.Win = [Bonus(8)];
                game.Loss = [Edges(ctx, 3 + ctx.Mean / 3, 10)];
                return (game, Play(game, ctx, 8, seconds, Say(game, "prompt").With("seconds", seconds).With("loss", Describe(game.Loss))));
            }
            case GameType.EdgeRush:
            {
                game.Target = 2 + ctx.Intensity / 2 + (ctx.Mean >= 7 ? 1 : 0);
                int seconds = (int)Math.Max(30, ctx.EdgeSeconds * (1 + (game.Target - 1) * 0.5) * (1.3 - ctx.Mean * 0.03));
                game.EdgesAtPhaseStart = ctx.EdgesThisStep;
                game.Win = [Bonus(game.Target * 2), Rest(20)];
                game.Loss = [Torment(ctx, 60 + ctx.Mean * 5)];
                return (game, Play(game, ctx, 8, seconds, Say(game, "prompt").With("target", game.Target).With("seconds", seconds)));
            }
            case GameType.RedLightGreenLight:
            {
                game.Rounds = 3 + ctx.Mean / 3;
                game.Win = [Bonus(8)];
                game.Loss = [Torment(ctx, 60 + ctx.Mean * 5)];
                game.Phase = 1;
                return (game, GreenOrRed(game, ctx));
            }
            case GameType.Chicken:
            {
                game.Target = 60 + ctx.Mean * 3;
                game.Loss = [Edges(ctx, 3, 10), Penalty(10)];
                game.Input = GameInput.Choices; game.Choices = [new("Bail now", 1)];
                return (game, Play(game, ctx, 8, game.Target, Say(game, "prompt"), answerReady: true));
            }
            case GameType.EdgeWager:
            {
                game.Input = GameInput.Choices;
                game.Choices = Enumerable.Range(1, 5).Select(x => new GameChoice($"Bet {x}", x)).ToList();
                return (game, Ask(game, Say(game, "prompt").With("seconds", WagerSeconds(1)).With("left", WagerSeconds(5))));
            }
            case GameType.TwoEnvelopes:
            {
                var mercy = new List<Consequence> { Rest(45), Bonus(8) };
                var cruelty = r.Next(0, 2) == 0 ? new List<Consequence> { Torment(ctx, 90 + ctx.Mean * 5) } : [Edges(ctx, 5, 10)];
                game.Secret = r.Next(1, 3); // the envelope holding mercy
                game.Win = mercy; game.Loss = cruelty;
                double truthful = ctx.Mood switch { DommeMood.Cruel => 30, DommeMood.Playful or DommeMood.Teasing => 70, _ => 55 };
                int hinted = r.Chance(truthful) ? game.Secret : 3 - game.Secret;
                game.Input = GameInput.Choices; game.Choices = [new("Envelope 1", 1), new("Envelope 2", 2)];
                return (game, Ask(game, Say(game, "prompt").With("win", Describe(mercy)).With("loss", Describe(cruelty)).With("answer", hinted)));
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
                return GameStep.Over(new(true, Result(game, "won"), game.Win));
            case GameType.EdgeEndurance when game.Phase == 0:
                game.Phase = 1;
                game.PhaseStartedAtUtc = ctx.Now;
                game.Input = GameInput.Choices; game.Choices = [new("I had to let go", 0)];
                return GameStep.Continue(Step(0, StepCategory.Hold), Say(game, "hold").With("target", game.Target), game.Target);
            case GameType.ArousalChallenge:
                return GameStep.Over(new(false, Result(game, "lost"), game.Loss));
            case GameType.EdgeRush:
            case GameType.EdgeWager when game.Phase == 1:
            {
                int made = edgesThisStep - game.EdgesAtPhaseStart;
                if (made < game.Target)
                    return GameStep.Continue(Step(8, StepCategory.Game) with { Method = ctx.StrokeMethod },
                        new Utterance(SpeechAct.GameProgress) { Tags = [$"game:{game.Type}"], Instruction = true }.With("made", made).With("target", game.Target), null);
                return GameStep.Over(new(true, Result(game, "won").With("target", game.Target), game.Win));
            }
            case GameType.RedLightGreenLight when game.Phase % 2 == 0:
                return GameStep.Over(new(false, Result(game, "lost", "edge-on-red"), game.Loss));
            case GameType.Chicken:
                return GameStep.Over(new(false, Result(game, "lost", "edged"), game.Loss));
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
                return GameStep.Over(new(false, Result(game, "lost", "timeout"), game.Loss));
            case GameType.RaceToEdge:
                return GameStep.Over(new(false, Result(game, "lost", "timeout"), game.Loss));
            case GameType.EdgeEndurance:
                return GameStep.Over(new(true, Result(game, "won").With("target", game.Target), game.Win));
            case GameType.ArousalChallenge:
                return GameStep.Over(new(true, Result(game, "won"), game.Win));
            case GameType.EdgeRush:
            case GameType.EdgeWager:
                return GameStep.Over(new(false, Result(game, "lost", "timeout").With("target", game.Target), game.Loss));
            case GameType.RedLightGreenLight:
                if (game.Phase < game.Rounds * 2)
                {
                    game.Phase++;
                    return GreenOrRed(game, ctx);
                }
                game.Input = GameInput.Choices;
                game.Choices = [new("I obeyed every call", 1), new("I slipped", 0)];
                return GameStep.Continue(Step(0, StepCategory.Game), Say(game, "ask").With("target", game.Rounds));
            case GameType.Chicken:
                return GameStep.Over(new(true, Result(game, "won", "survived"), [Bonus(game.Target / 5)]));
            default:
                return GameStep.Over(new(false, Result(game, "lost", "timeout"), game.Loss));
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
                return GameStep.Over(new(won, Result(game, won ? "won" : "lost").With("reveal", reveal), won ? game.Win : game.Loss, answer));
            }
            case GameType.NumberGuess:
            {
                if (answer == game.Secret) return GameStep.Over(new(true, Result(game, "won").With("reveal", $"{game.Secret}."), game.Win, answer));
                int off = Math.Abs(answer - game.Secret);
                return GameStep.Over(new(false, Result(game, "lost").With("reveal", $"It was {game.Secret}. You were {off} off."), [Edges(ctx, Math.Min(5, off), 0)], answer));
            }
            case GameType.DoubleOrNothing:
                if (answer == 2) return GameStep.Over(new(true, Result(game, "neutral", "took-it"), game.Options, answer, Neutral: true));
                string side = game.Secret == 0 ? "Heads." : "Tails.";
                return answer == game.Secret
                    ? GameStep.Over(new(true, Result(game, "won").With("reveal", side), game.Win, answer))
                    : GameStep.Over(new(false, Result(game, "lost").With("reveal", side), game.Loss, answer));
            case GameType.PickYourPoison:
                return GameStep.Over(new(false, Result(game, "neutral").With("answer", answer), [game.Options[answer - 1]], answer, Neutral: true));
            case GameType.RerollGamble:
            {
                if (answer == 0 || game.Data >= game.Target)
                {
                    var fate = game.Options[game.Secret];
                    var said = Result(game, "neutral", answer == 0 ? "accepted" : "out-of-rerolls").With("fate", fate.Description);
                    return GameStep.Over(new(game.Secret >= game.Options.Count / 2, said, [fate], answer, Neutral: true));
                }
                game.Data++;
                // Each reroll leans a little further toward the worse fates at the front of the list.
                int roll = ctx.Random.Next(0, game.Options.Count);
                game.Secret = Math.Max(0, roll - (game.Data - 1));
                var reroll = Say(game, "reroll").With("made", game.Data).With("fate", game.Options[game.Secret].Description);
                if (game.Data >= game.Target) { game.Choices = [new("Accept", 0)]; reroll = reroll.Tagged("last"); }
                return GameStep.Continue(Step(0, StepCategory.Game), reroll);
            }
            case GameType.QuickMath:
                return answer == game.Secret
                    ? GameStep.Over(new(true, Result(game, "won"), game.Win, answer))
                    : GameStep.Over(new(false, Result(game, "lost").With("reveal", $"It was {game.Secret}."), game.Loss, answer));
            case GameType.EdgeEndurance:
                return GameStep.Over(new(false, Result(game, "lost", "let-go"), game.Loss, answer));
            case GameType.RedLightGreenLight:
                return answer == 1
                    ? GameStep.Over(new(true, Result(game, "won"), game.Win, answer))
                    : GameStep.Over(new(false, Result(game, "lost", "confessed"), game.Loss, answer));
            case GameType.Chicken:
            {
                int seconds = (int)Math.Max(0, (ctx.Now - game.PhaseStartedAtUtc).TotalSeconds);
                int points = seconds / 10 * 2;
                var bailed = points > 0 ? Result(game, "won", "bailed").With("seconds", seconds).With("made", points) : Result(game, "lost", "coward");
                return GameStep.Over(new(points > 0, bailed, points > 0 ? [Bonus(points)] : [], answer));
            }
            case GameType.EdgeWager when game.Phase == 0:
            {
                game.Target = answer;
                game.Phase = 1;
                game.EdgesAtPhaseStart = ctx.EdgesThisStep;
                game.Win = [Bonus(answer * 3)];
                game.Loss = [Torment(ctx, 20 * answer + ctx.Mean * 3)];
                game.Input = GameInput.None; game.Choices = [];
                int seconds = WagerSeconds(answer);
                return GameStep.Continue(Step(8, StepCategory.Game) with { Method = ctx.StrokeMethod }, Say(game, "bet").With("bet", answer).With("seconds", seconds), seconds);
            }
            case GameType.TwoEnvelopes:
            {
                bool won = answer == game.Secret;
                return GameStep.Over(new(won, Result(game, won ? "won" : "lost"), won ? game.Win : game.Loss, answer));
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Stakes in her words, for prompts and choice labels. The session's points never reach him:
    /// they are her clock, so a points stake becomes mercy or a longer night.
    /// </summary>
    public static string Describe(IReadOnlyList<Consequence> consequences)
        => consequences.Count == 0 ? "nothing"
            : string.Join(" and ", consequences.OrderBy(c => c.Kind is ConsequenceKind.Points or ConsequenceKind.PermissionOdds ? 1 : 0).Select(Describe));

    private static string Describe(Consequence c) => c.Description != "" ? c.Description : c.Kind switch
    {
        ConsequenceKind.Rest => $"{c.Amount} seconds of rest",
        ConsequenceKind.ExtraEdges => c.Hold > 0 ? $"{c.Amount} extra edges held for {c.Hold} seconds each" : $"{c.Amount} extra edges",
        ConsequenceKind.Torment => $"{c.Amount} seconds of torment",
        ConsequenceKind.Points => c.Amount switch
        {
            >= 10 => "a shorter night for you",
            >= 6 => "I go easier on you for the rest of tonight",
            >= 0 => "a little mercy",
            > -6 => "I keep you a little longer",
            _ => "a much longer night",
        },
        ConsequenceKind.PermissionOdds => c.Amount >= 0 ? "a better chance at a good ending" : "a worse chance at a good ending",
        _ => "",
    };

    /// <summary>The stakes as the engine applies them, for transcripts and the session record.</summary>
    public static string Mechanics(IReadOnlyList<Consequence> consequences)
        => consequences.Count == 0 ? "nothing" : string.Join(", ", consequences.Select(c => c.Kind switch
        {
            ConsequenceKind.Rest => $"{c.Amount}s rest",
            ConsequenceKind.ExtraEdges => c.Hold > 0 ? $"{c.Amount} edges held {c.Hold}s" : $"{c.Amount} edges",
            ConsequenceKind.Torment => $"{c.Amount}s torment",
            ConsequenceKind.Points => $"{c.Amount:+0;-0} points",
            ConsequenceKind.PermissionOdds => $"{c.Amount:+0;-0}% ending odds",
            _ => c.Kind.ToString(),
        }));

    private static Consequence Rest(int seconds) => new(ConsequenceKind.Rest, seconds);
    // Stakes are in the currency of what she is doing to him: in the middle of testing his control
    // a loss is more edges; in the middle of overwhelm it is more of that.
    private static Consequence Torment(GameContext ctx, int seconds)
        => ctx.Aim == Plan.Purpose.Control && ctx.DommeOwnsEdges ? new(ConsequenceKind.ExtraEdges, Math.Clamp(seconds / 25, 2, 6), 10) : new(ConsequenceKind.Torment, seconds);
    private static Consequence Bonus(int points) => new(ConsequenceKind.Points, points);
    private static Consequence Penalty(int points) => new(ConsequenceKind.Points, -points);
    private static Consequence Edges(GameContext ctx, int count, int hold)
        => ctx.DommeOwnsEdges && ctx.Aim != Plan.Purpose.Overwhelm ? new(ConsequenceKind.ExtraEdges, count, hold) : new(ConsequenceKind.Torment, count * 20 + hold);

    private static List<Consequence> Fates(GameContext ctx) =>
    [
        new(ConsequenceKind.PermissionOdds, -40, 0, "denial becomes very likely tonight"),
        new(ConsequenceKind.Torment, 120, 0, "two minutes of your worst"),
        ctx.DommeOwnsEdges ? new(ConsequenceKind.ExtraEdges, 5, 20, "five edges, each held for twenty seconds") : new(ConsequenceKind.Torment, 90, 0, "ninety seconds of torment"),
        new(ConsequenceKind.Points, 10, 0, "a little mercy: I go easier on you for the rest of tonight"),
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

    /// <summary>What she says at a stage of a game: the narrator renders the rules and her words.</summary>
    private static Utterance Say(GameState game, string stage)
        => new(SpeechAct.GamePrompt) { Tags = [$"game:{game.Type}", $"stage:{stage}"], Instruction = true };

    private static Utterance Result(GameState game, string result, params string[] detail)
        => new(SpeechAct.GameResult) { Tags = [$"game:{game.Type}", $"result:{result}", .. detail.Select(x => $"detail:{x}")] };

    private static GameStep Ask(GameState game, Utterance say, int timerSeconds = 0)
        => GameStep.Continue(Step(0, StepCategory.Game), say, timerSeconds);

    private static GameStep Play(GameState game, GameContext ctx, int speed, int timerSeconds, Utterance say, bool answerReady = false)
    {
        if (!answerReady) { game.Input = GameInput.None; game.Choices = []; }
        return GameStep.Continue(Step(speed, StepCategory.Game) with { Method = ctx.StrokeMethod }, say, timerSeconds);
    }

    private static GameStep GreenOrRed(GameState game, GameContext ctx)
    {
        bool green = game.Phase % 2 == 1;
        int seconds = green ? 5 + ctx.Random.Next(0, 10) : 3 + ctx.Random.Next(0, 6);
        var say = Say(game, green ? "green" : "red");
        if (game.Phase == 1) say = say.Tagged("first").With("target", game.Rounds);
        return GameStep.Continue(Step(green ? 8 : 0, StepCategory.Game) with { Method = green ? ctx.StrokeMethod : null }, say, seconds);
    }

    private static Directive Step(int speed, StepCategory category) => new()
    {
        Kind = InstructionKind.FreeText, Category = category, Speed = speed, Phase = SessionPhase.Intense,
    };
}
