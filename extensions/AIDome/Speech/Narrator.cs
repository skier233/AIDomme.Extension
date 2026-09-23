using System.Text.RegularExpressions;
using AIDome.Sessions;

namespace AIDome.Speech;

/// <summary>A story she is in the middle of telling.</summary>
public sealed class StoryInProgress
{
    public string Module { get; set; } = "";
    public int Next { get; set; }
}

/// <summary>
/// What she has said: every line used this session (so none repeats), modules to record for
/// cross-session cooldowns, a story in progress, and when she last spoke. Saved with the session.
/// </summary>
public sealed class SpeechMemory
{
    public List<string> Used { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    /// <summary>Modules with cross-session cooldowns used and not yet recorded.</summary>
    public List<string> Pending { get; set; } = [];
    public StoryInProgress? Story { get; set; }
    public DateTime? LastLineUtc { get; set; }
    public DateTime? LastCommandUtc { get; set; }
    public DateTime? LastChatterUtc { get; set; }
    public DateTime? ChatterDueUtc { get; set; }
    public bool LastWasChatter { get; set; }
    public bool LastNoBridge { get; set; }
    public int ChatterLines { get; set; }
}

/// <summary>A line she says: the words, what it was, and the module it came from.</summary>
public sealed record Spoken(string Text, SpeechAct Act, SpeechStream Stream, bool Instruction, string? Module = null, bool NoBridge = false);

/// <summary>Everything the narrator reads when it chooses words.</summary>
public sealed class NarrationContext
{
    public required Facts Facts { get; init; }
    public required SpeechMemory Memory { get; init; }
    public Recall Recall { get; init; } = new();
    public SessionPersona Persona { get; init; } = new();
    public required IRandom Random { get; init; }
    public DateTime Now { get; init; }
    /// <summary>False when only her orders are wanted (homework written out): no asides at all.</summary>
    public bool Chatter { get; init; } = true;
}

/// <summary>Turns utterances into her words. The rule narrator reads the line files; others could render the same specs.</summary>
public interface INarrator
{
    /// <summary>Her line for an utterance, or null when she says nothing.</summary>
    Spoken? Render(Utterance u, NarrationContext c);
    /// <summary>The next line of the story in progress, or null when there is none.</summary>
    Spoken? ContinueStory(NarrationContext c);
    /// <summary>Starts a story that fits a quiet stretch of <paramref name="quietSeconds"/>, or null.</summary>
    Spoken? StartStory(NarrationContext c, int quietSeconds);
}

public sealed class RuleNarrator(LineLibrary library) : INarrator
{
    public static RuleNarrator Default { get; } = new(LineLibrary.Default);

    /// <summary>Acts that must always produce words: without a matching line she falls back to the bare content.</summary>
    private static readonly HashSet<SpeechAct> Required =
    [
        SpeechAct.Instruct, SpeechAct.Hold, SpeechAct.Cooldown, SpeechAct.Resume, SpeechAct.Lube, SpeechAct.FreePlay, SpeechAct.Price,
        SpeechAct.OfferDecision, SpeechAct.Finish, SpeechAct.Denied, SpeechAct.PunishmentEnding, SpeechAct.Blocked, SpeechAct.BegCumDenied,
        SpeechAct.BegRestDenied, SpeechAct.BegRestGranted, SpeechAct.OfferMade, SpeechAct.GamePrompt, SpeechAct.GameProgress, SpeechAct.GameResult,
        SpeechAct.ChallengeFailed, SpeechAct.PromiseAnswer, SpeechAct.FollowUpAnswer, SpeechAct.Retaliation, SpeechAct.TimeUp, SpeechAct.SessionEnd,
    ];

    private static readonly Regex SlotPattern = new(@"\[([A-Za-z.]+)\]", RegexOptions.Compiled);

    public Spoken? Render(Utterance u, NarrationContext c)
    {
        string core = Core(u, c.Random);
        var memory = c.Memory;
        var facts = u.Slots.Count > 0 ? c.Facts.With(u.Slots) : c.Facts;
        var used = memory.Used.ToHashSet();
        var candidates = new List<(LineModule Module, List<int> Variants)>();
        var fallback = new List<(LineModule Module, List<int> Variants)>();
        foreach (var m in library.For(u.Act))
        {
            if (m.IsStory || m.Stream is { } stream && stream != u.Stream && u.Act is SpeechAct.Chatter or SpeechAct.Interject) continue;
            if (!CooldownOk(m, c) || !Conditions.Hold(m.When, facts, u)) continue;
            var fresh = new List<int>();
            var any = new List<int>();
            for (int i = 0; i < m.Variants.Length; i++)
            {
                if (!Fillable(m.Variants[i], u, c, core)) continue;
                any.Add(i);
                if (!used.Contains(Key(m, i))) fresh.Add(i);
            }
            if (fresh.Count > 0) candidates.Add((m, fresh));
            if (any.Count > 0) fallback.Add((m, any));
        }
        // Lines never repeat within a session; when she must speak and every fitting line is used,
        // she reuses one rather than go silent.
        if (candidates.Count == 0 && Required.Contains(u.Act)) candidates = fallback;
        string text;
        LineModule? chosen = null;
        if (candidates.Count > 0)
        {
            var pick = c.Random.Weighted(candidates.Select(x => (x, x.Module.Weight)).ToList(), candidates[0]);
            int variant = c.Random.Pick(pick.Variants);
            chosen = pick.Module;
            text = Fill(chosen.Variants[variant], u, c, core);
            if (u.Instruction && core.Length > 0 && !Mentions(chosen.Variants[variant], "instruction") && !Mentions(chosen.Variants[variant], "rules"))
                text = $"{text} {core}";
            Remember(chosen, variant, c);
        }
        else if (Required.Contains(u.Act) && core.Length > 0) text = core;
        else return null;
        text = Tidy(text);
        return text.Length == 0 ? null : new(text, u.Act, u.Stream, u.Instruction, chosen?.Id, chosen?.NoBridge ?? false);
    }

    public Spoken? ContinueStory(NarrationContext c)
    {
        var story = c.Memory.Story;
        if (story is null) return null;
        var module = library.For(SpeechAct.Chatter).FirstOrDefault(x => x.Id == story.Module);
        if (module is null || story.Next >= module.Lines.Length)
        {
            c.Memory.Story = null;
            return null;
        }
        var u = new Utterance(SpeechAct.Chatter) { Stream = SpeechStream.Chatter };
        string text = Tidy(Fill(module.Lines[story.Next], u, c, ""));
        story.Next++;
        if (story.Next >= module.Lines.Length) c.Memory.Story = null;
        return new(text, SpeechAct.Chatter, SpeechStream.Chatter, false, module.Id, module.NoBridge);
    }

    public Spoken? StartStory(NarrationContext c, int quietSeconds)
    {
        var u = new Utterance(SpeechAct.Chatter) { Stream = SpeechStream.Chatter };
        var stories = library.For(SpeechAct.Chatter)
            .Where(m => m.IsStory && m.Needs <= quietSeconds && CooldownOk(m, c) && Conditions.Hold(m.When, c.Facts, u)
                && m.Lines.All(line => Fillable(line, u, c, "")))
            .ToList();
        if (stories.Count == 0) return null;
        var module = c.Random.Weighted(stories.Select(x => (x, x.Weight)).ToList(), stories[0]);
        Remember(module, 0, c);
        c.Memory.Story = new() { Module = module.Id, Next = 0 };
        return ContinueStory(c);
    }

    /// <summary>Seconds between the lines of the story in progress.</summary>
    public int StorySpacing(SpeechMemory memory)
        => memory.Story is { } story && library.For(SpeechAct.Chatter).FirstOrDefault(x => x.Id == story.Module) is { } m ? m.Spacing : 12;

    // ── Internals ──

    /// <summary>The exact part: the instruction, a game's rules, the bare content of required acts.</summary>
    private static string Core(Utterance u, IRandom r)
    {
        if (u.Act == SpeechAct.GamePrompt) return GameGrammar.Rules(u) is { Length: > 0 } rules ? rules : "";
        if (u.Act == SpeechAct.GameProgress) return GameGrammar.Progress(u);
        return u.Directive is null ? "" : Grammar.Instruction(u, r);
    }

    private static bool CooldownOk(LineModule m, NarrationContext c)
    {
        if (m.Cooldown == Cooldown.Session) return true;
        if (c.Memory.Modules.Contains(m.Id)) return false;
        if (!c.Recall.Usage.TryGetValue(m.Id, out var use)) return true;
        return m.Cooldown switch
        {
            Cooldown.Once => false,
            Cooldown.Rare => use.SessionsAgo >= 20 && (c.Now - use.LastUsedUtc).TotalDays >= 60,
            _ => use.SessionsAgo >= 5 && (c.Now - use.LastUsedUtc).TotalDays >= 14,
        };
    }

    private static void Remember(LineModule m, int variant, NarrationContext c)
    {
        c.Memory.Used.Add(Key(m, variant));
        if (!c.Memory.Modules.Contains(m.Id)) c.Memory.Modules.Add(m.Id);
        if (m.Cooldown != Cooldown.Session && !c.Memory.Pending.Contains(m.Id)) c.Memory.Pending.Add(m.Id);
        // Keep the session's memory bounded.
        if (c.Memory.Used.Count > 2000) c.Memory.Used.RemoveRange(0, 500);
    }

    private static string Key(LineModule m, int variant) => $"{m.Id}#{variant}";

    private static bool Mentions(string text, string slot)
        => SlotPattern.Matches(text).Any(x => string.Equals(x.Groups[1].Value, slot, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every slot in the line has a value.</summary>
    private static bool Fillable(string text, Utterance u, NarrationContext c, string core)
    {
        foreach (Match match in SlotPattern.Matches(text))
            if (Value(match.Groups[1].Value, u, c, core, peek: true) is null) return false;
        return true;
    }

    private static string Fill(string text, Utterance u, NarrationContext c, string core)
        => SlotPattern.Replace(text, match =>
        {
            string name = match.Groups[1].Value;
            string value = Value(name, u, c, core, peek: false) ?? "";
            if (value.Length == 0) return "";
            // [Slot] capitalises, [slot] keeps the value's case except for instructions, which read mid-sentence.
            if (char.IsUpper(name[0])) return Grammar.Sentence(value);
            return string.Equals(name, "instruction", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "rules", StringComparison.OrdinalIgnoreCase)
                ? Grammar.Lower(value) : value;
        });

    private static string? Value(string name, Utterance u, NarrationContext c, string core, bool peek)
    {
        switch (name.ToLowerInvariant())
        {
            case "instruction":
                return core.Length > 0 && u.Act is not (SpeechAct.GamePrompt or SpeechAct.GameProgress) ? core : null;
            case "rules":
                return u.Act is SpeechAct.GamePrompt or SpeechAct.GameProgress && core.Length > 0 ? core
                    : u.Slots.TryGetValue("rules", out var rules) ? rules : null;
            case "name":
                var names = Names(c.Persona);
                return peek ? names[0] : c.Random.Pick(names);
        }
        if (u.Slots.TryGetValue(name, out var slot) && slot.Length > 0) return slot;
        if (u.Directive is { } d && Grammar.Slots(d).TryGetValue(name, out var fromDirective) && fromDirective.Length > 0) return fromDirective;
        return c.Facts.Text(name);
    }

    /// <summary>What she calls him: his chosen names, or hers, depending on how much she humiliates him.</summary>
    public static IReadOnlyList<string> Names(SessionPersona persona)
    {
        if (persona.Names.Length > 0) return persona.Names;
        return persona.Humiliation switch
        {
            >= 7 => ["toy", "pathetic boy", "loser"],
            >= 4 => ["pet", "toy", "boy"],
            _ => ["pet", "boy"],
        };
    }

    private static string Tidy(string text)
    {
        text = Regex.Replace(text, @"\s{2,}", " ").Trim();
        text = Regex.Replace(text, @"\s+([.,!?])", "$1");
        // A slot ending a sentence before the line's own full stop: one stop, but keep ellipses.
        text = Regex.Replace(text, @"([!?])\.(?!\.)", "$1");
        text = Regex.Replace(text, @"(?<!\.)\.\.(?!\.)", ".");
        return text;
    }
}
