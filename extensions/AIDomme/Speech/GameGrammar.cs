using AIDomme.Data;

namespace AIDomme.Speech;

/// <summary>
/// The exact rules of each game at each stage, from the values the game supplies. Like the
/// instruction grammar, this is the part that must be precise; her lines wrap it with an intro,
/// a taunt or a reaction and use it as the <c>[rules]</c> slot.
/// </summary>
public static class GameGrammar
{
    public static string Rules(Utterance u)
    {
        string game = Tag(u, "game"), stage = Tag(u, "stage");
        string S(string key) => u.Slots.TryGetValue(key, out var v) ? v : "";
        string grip = u.Directive?.Method is { } m ? Grammar.Method(m) : "Stroke";
        return (game, stage) switch
        {
            (nameof(GameType.CoinFlip), "prompt") => $"I'm flipping a coin. Call it. Right, and {S("win")}. Wrong, and {S("loss")}.",
            (nameof(GameType.NumberGuess), "prompt") => $"I'm thinking of a number between 1 and {S("target")}. Guess it and {S("win")}. Miss, and "
                + (u.Has("edges") ? "you edge once for every step you were off." : "you pay twenty seconds of torment for every step you were off."),
            (nameof(GameType.CardDraw), "prompt") => $"I'm drawing a card you can't see. High or low? Right, and {S("win")}. Wrong, and {S("loss")}.",
            (nameof(GameType.DoubleOrNothing), "prompt") => $"You've earned {S("win")}. Take it, or call a coin for double. Lose the call and you get nothing but {S("loss")}.",
            (nameof(GameType.PickYourPoison), "prompt") => $"Pick your poison. One: {S("win")}. Two: {S("loss")}.",
            (nameof(GameType.RerollGamble), "prompt") => $"Your fate: {S("fate")}. Accept it, or reroll and let the house tighten the screws. You get {S("target")} rerolls at most.",
            (nameof(GameType.RerollGamble), "reroll") => u.Has("last") ? $"Reroll {S("made")}, your last. Your fate: {S("fate")}." : $"Reroll {S("made")}. New fate: {S("fate")}. Accept, or push your luck?",
            (nameof(GameType.QuickMath), "prompt") => $"Keep stroking and tell me: what's {S("rules")}? You have {S("seconds")} seconds.",
            (nameof(GameType.RaceToEdge), "prompt") => $"{grip}. Reach the edge within {S("seconds")} seconds and record it. Too slow, and {S("loss")}.",
            (nameof(GameType.EdgeEndurance), "prompt") => $"{grip}. Record it when you reach the edge, then hold it for {S("target")} seconds without going over. If you have to let go, admit it.",
            (nameof(GameType.EdgeEndurance), "hold") => $"Hold it right there for {S("target")} seconds. Don't go over.",
            (nameof(GameType.ArousalChallenge), "prompt") => $"{grip}, fast, for {S("seconds")} seconds, and do not edge. Edge before time is up and {S("loss")}.",
            (nameof(GameType.EdgeRush), "prompt") => $"{Grammar.Sentence(S("target"))} edges in {S("seconds")} seconds. {grip}. Every one you record counts.",
            (nameof(GameType.RedLightGreenLight), "green") => (u.Has("first") ? $"Red light, green light. {S("target")} rounds. Stroke on green, hands off on red. An edge on red loses. " : "") + "GREEN LIGHT. Stroke, fast.",
            (nameof(GameType.RedLightGreenLight), "red") => "RED LIGHT. Hands off. Now.",
            (nameof(GameType.RedLightGreenLight), "ask") => $"All {S("target")} rounds done. Did you obey every call? Be honest.",
            (nameof(GameType.Chicken), "prompt") => $"Chicken. {grip}, fast. Every ten seconds you last earns you a little mercy. Bail whenever you like. Edge before you bail and you lose all of it, and more.",
            (nameof(GameType.EdgeWager), "prompt") => $"Place a bet: how many edges can you give me in {S("seconds")} to {S("left")} seconds? Bet high and make it, and I go easier on you. Miss, and every edge you bet comes back as torment.",
            (nameof(GameType.EdgeWager), "bet") => $"You bet {S("bet")}. {grip}. {S("seconds")} seconds, starting now.",
            (nameof(GameType.TwoEnvelopes), "prompt") => $"Two envelopes. One holds mercy: {S("win")}. The other holds {S("loss")}.",
            _ => "",
        };
    }

    public static string Progress(Utterance u)
        => u.Slots.TryGetValue("made", out var made) && u.Slots.TryGetValue("target", out var target) ? $"{made} of {target}." : "";

    public static string Tag(Utterance u, string prefix)
        => u.Tags.FirstOrDefault(x => x.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase))?[(prefix.Length + 1)..] ?? "";
}
