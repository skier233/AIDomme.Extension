using AIDome.Data;
using AIDome.Sessions;

namespace AIDome.Speech;

/// <summary>
/// The core tier: the instruction itself, rendered exactly from a directive (method, pace, time,
/// edges, hold). Lines may wrap it with her words but never change it. Plain phrasing is allowed
/// to repeat: "stroke" is "stroke".
/// </summary>
public static class Grammar
{
    /// <summary>The method as an imperative phrase, without a final period.</summary>
    public static string Method(MethodInfo? m)
        => m is null ? "Hands off"
            : m.IsUserChoice ? "Stroke however you like"
            : m.Description.Trim().TrimEnd('.', ' ') is { Length: > 0 } d ? d : $"Use {m.Name}";

    /// <summary>Pace words for a speed; empty when he sets his own pace or a toy sets it.</summary>
    public static string Pace(int speed, MethodInfo? m = null) => m is { ToyCategory: not ToyCategory.None } ? "" : speed switch
    {
        <= 0 => "",
        1 => "as slowly as you possibly can",
        2 => "nice and slow",
        3 => "slowly",
        4 => "at a steady pace",
        5 => "at a medium pace",
        6 => "a little faster",
        7 => "fast",
        8 => "nice and fast",
        9 => "as fast as you can",
        _ => "full speed",
    };

    public static string Duration(int seconds) => seconds switch
    {
        <= 0 => "a moment",
        < 55 => $"{seconds} seconds",
        < 70 => "a minute",
        < 100 => "a minute and a half",
        < 130 => "two minutes",
        < 165 => "two and a half minutes",
        _ => $"{(int)Math.Round(seconds / 60.0)} minutes",
    };

    public static string Times(int count) => count switch { 1 => "once", 2 => "twice", _ => $"{count} times" };
    public static string Edges(int count) => count == 1 ? "one edge" : $"{Number(count)} edges";

    public static string Number(int n) => n switch
    {
        0 => "zero", 1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven",
        8 => "eight", 9 => "nine", 10 => "ten", 11 => "eleven", 12 => "twelve", _ => n.ToString(),
    };

    /// <summary>Slots describing a directive, for lines to use by name.</summary>
    public static Dictionary<string, string> Slots(Directive d)
    {
        var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["method"] = d.Method is { IsUserChoice: false } m ? m.Name : "",
            ["grip"] = Lower(Method(d.Method)),
            ["pace"] = Pace(d.Speed, d.Method),
            ["seconds"] = d.DurationOrCount.ToString(),
        };
        if (d.Kind == InstructionKind.EdgeCount)
        {
            slots["count"] = Edges(d.DurationOrCount);
            slots["times"] = Times(d.DurationOrCount);
        }
        else if (d.DurationOrCount > 0) slots["time"] = Duration(d.DurationOrCount);
        if (d.HoldSeconds > 0) slots["hold"] = Duration(d.HoldSeconds);
        return slots;
    }

    /// <summary>The exact instruction for a directive, as a sentence or two.</summary>
    public static string Instruction(Utterance u, IRandom r)
    {
        var d = u.Directive;
        if (d is null) return "";
        string method = Method(d.Method);
        string pace = Pace(d.Speed, d.Method);
        string paced = pace.Length > 0 ? $"{method}, {pace}" : method;
        string time = Duration(d.DurationOrCount);
        return u.Act switch
        {
            SpeechAct.Hold => $"Hold it right there for {Duration(d.DurationOrCount)}. Don't go over.",
            SpeechAct.Cooldown => $"Hands off for {time}.",
            SpeechAct.Lube => "Get the lube and use plenty. Tell me when you're ready.",
            SpeechAct.Resume => $"Back to it. {paced}, {time} left.",
            SpeechAct.Finish when u.Has("ruined")
                => $"{Sentence(paced)}. Take yourself to the edge, then let go completely and keep your hands off while it happens. You have {time}. Record it when you cum.",
            SpeechAct.Finish => $"{Sentence(paced)}. You have {time} to cum. Record it when you do.",
            SpeechAct.PunishmentEnding => u.Slots.TryGetValue("ender", out var ender) ? ender : "",
            SpeechAct.Price => Price(d, paced, u),
            SpeechAct.Denied or SpeechAct.FreePlay or SpeechAct.OfferDecision => "",
            _ => d.Kind switch
            {
                InstructionKind.Rest => $"Hands off for {time}.",
                InstructionKind.EdgeCount => EdgeCount(d, paced, r, u),
                InstructionKind.TimedMethod when d.ForbidEdge => $"{Sentence(paced)}, for {time}, and do not edge.",
                InstructionKind.TimedMethod when d.CountEdges => $"{Sentence(paced)}, for {time}. Every edge you give me counts.",
                InstructionKind.TimedMethod => r.Next(0, 3) switch
                {
                    0 => $"{Sentence(paced)}. Keep it up for {time}.",
                    _ => $"{Sentence(paced)}, for {time}.",
                },
                _ => "",
            },
        };
    }

    private static string EdgeCount(Directive d, string paced, IRandom r, Utterance u)
    {
        int n = d.DurationOrCount;
        string hold = d.HoldSeconds <= 0 ? ""
            : n > 1 ? $" Hold the last one for {Duration(d.HoldSeconds)}." : $" When you get there, hold it for {Duration(d.HoldSeconds)}.";
        // She has waited long enough.
        if (u.Has("hurry")) return $"Faster. {Sentence(paced)}. Get to the edge.";
        // One edge after another with the count kept from him: "again", and only what changed.
        if (n == 1 && (u.Has("again") || u.Has("again-same")))
        {
            string left = u.Tags.FirstOrDefault(x => x.StartsWith("left:")) is { } tag && int.TryParse(tag[5..], out int more)
                ? more == 1 ? " This is the last one." : $" {Sentence(Number(more))} to go."
                : "";
            return u.Has("again-same") ? $"Again.{left}{hold}" : $"{Sentence(paced)}, and edge again.{left}{hold}";
        }
        string edge = n == 1
            ? r.Next(0, 2) == 0 ? "and bring yourself to the edge" : "and edge for me"
            : r.Next(0, 2) == 0 ? $"and edge {Times(n)}" : $"and give me {Edges(n)}";
        return $"{Sentence(paced)}, {edge}.{hold}";
    }

    private static string Price(Directive d, string paced, Utterance u)
    {
        int left = d.DurationOrCount;
        string edges = left == 1 ? "one more edge" : $"{Number(left)} more edges";
        string hold = d.HoldSeconds > 0 ? $", and hold the last one for {Duration(d.HoldSeconds)}" : "";
        return d.Kind == InstructionKind.EdgeCount
            ? $"{Sentence(paced)}: {edges}{hold}."
            : $"{Sentence(edges)}{hold}. Your way, but record every one.";
    }

    public static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    public static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
