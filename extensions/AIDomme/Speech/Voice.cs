using AIDomme.Data;
using AIDomme.Sessions;

namespace AIDomme.Speech;

/// <summary>
/// The one scheduler for both of her streams. Commands are always spoken on time; chatter fills
/// the quiet between them and yields: it never starts a story that the next command would cut
/// off, keeps a gap after every line, stays quiet just before a command it can see coming, and
/// bridges back when a command follows chatter too closely.
/// </summary>
public sealed class Voice(INarrator narrator)
{
    public static Voice Default { get; } = new(RuleNarrator.Default);

    /// <summary>No chatter within this long after a command, or before a timer about to fire.</summary>
    public const int CommandQuiet = 8;
    /// <summary>Minimum gap between any two of her lines, except a command right after a command.</summary>
    public const int LineGap = 5;

    public INarrator Narrator => narrator;

    /// <summary>
    /// Speaks what a transition asked for. Optional reactions are thinned by how talkative she is
    /// tonight; a command straight after chatter gets a bridge; a story in progress is dropped.
    /// </summary>
    public List<Spoken> Speak(IEnumerable<Utterance> utterances, NarrationContext c)
    {
        var lines = new List<Spoken>();
        var memory = c.Memory;
        // In one moment her orders come first; an aside follows them instead of needing a bridge.
        var ordered = utterances.OrderBy(u => u.Stream == SpeechStream.Chatter ? 1 : 0).ToList();
        foreach (var u in ordered)
        {
            if (!Wanted(u, c) || (!c.Chatter && u.Stream == SpeechStream.Chatter)) continue;
            // She doesn't break off a story for a remark she could skip.
            if (memory.Story is not null && Optional(u.Act)) continue;
            // Straight after an aside, a command gets a word that brings him back to her orders.
            Spoken? bridge = null;
            if (u.Stream == SpeechStream.Command && u.Instruction && memory.LastWasChatter && !memory.LastNoBridge
                && memory.LastChatterUtc is DateTime last && (c.Now - last).TotalSeconds < 12)
                bridge = narrator.Render(Utterance.Of(SpeechAct.Bridge, memory.Story is null ? "after-chatter" : "interrupted"), c);
            if (u.Stream == SpeechStream.Command) memory.Story = null;
            if (narrator.Render(u, c) is not { } spoken) continue;
            // A line that already opens with a word of its own ("Now.", "Right.") needs no bridge.
            if (bridge is not null && !OpensShort(spoken.Text)) spoken = spoken with { Text = $"{bridge.Text} {spoken.Text}" };
            lines.Add(spoken);
            Heard(memory, spoken, c.Now);
        }
        return lines;
    }

    /// <summary>When she may next say something unprompted, or null when she should stay quiet.</summary>
    public DateTime? NextChatter(SessionState s, DateTime now)
    {
        if (!MayChat(s)) return null;
        var memory = s.Speech;
        var persona = s.Persona;
        var due = memory.ChatterDueUtc ?? (memory.LastChatterUtc ?? s.StartedAtUtc).AddSeconds(Gap(persona, s));
        if (memory.LastCommandUtc is DateTime command && due < command.AddSeconds(CommandQuiet)) due = command.AddSeconds(CommandQuiet);
        if (memory.LastLineUtc is DateTime line && due < line.AddSeconds(LineGap)) due = line.AddSeconds(LineGap);
        // Never right before the next command she can see coming.
        if (s.TimerDueUtc is DateTime timer && !(s.Current?.HiddenTimer ?? false) && due > timer.AddSeconds(-CommandQuiet)) return null;
        return due < now ? now : due;
    }

    /// <summary>A quiet moment arrived: maybe a line of chatter, then when the next may come.</summary>
    public List<Spoken> Tick(SessionState s, NarrationContext c)
    {
        var lines = new List<Spoken>();
        var memory = c.Memory;
        if (!MayChat(s)) return lines;
        int quiet = Quiet(s, c.Now);
        Spoken? spoken = null;
        if (memory.Story is not null)
        {
            spoken = quiet >= LineGap ? narrator.ContinueStory(c) : null;
            if (spoken is null) memory.Story = null;
        }
        else
        {
            double storyChance = 10 + s.Persona.Talkativeness * 1.5 + s.Persona.Manipulation + (Psych(s) ? 25 : 0);
            if (!ExpectingEdge(s) && c.Random.Chance(storyChance)) spoken = narrator.StartStory(c, quiet);
            if (spoken is null)
            {
                // During stimulation she comments on what he is doing; otherwise she talks.
                bool working = s.Current is { Category: not (StepCategory.Rest or StepCategory.Hold or StepCategory.FreePlay or StepCategory.Offer), Method: not null };
                var act = working && c.Random.Chance(55) ? SpeechAct.Interject : SpeechAct.Chatter;
                spoken = narrator.Render(new Utterance(act) { Stream = SpeechStream.Chatter }, c)
                    ?? (act == SpeechAct.Interject ? narrator.Render(new Utterance(SpeechAct.Chatter) { Stream = SpeechStream.Chatter }, c) : null);
            }
        }
        if (spoken is not null)
        {
            lines.Add(spoken);
            Heard(memory, spoken, c.Now);
            memory.ChatterLines++;
        }
        // The next moment: soon when a story continues, otherwise after her usual gap.
        double gap = memory.Story is not null && narrator is RuleNarrator rules ? rules.StorySpacing(memory) : Gap(s.Persona, s) * (0.7 + c.Random.NextDouble() * 0.6);
        memory.ChatterDueUtc = c.Now.AddSeconds(gap);
        return lines;
    }

    private static void Heard(SpeechMemory memory, Spoken spoken, DateTime now)
    {
        memory.LastLineUtc = now;
        memory.LastNoBridge = spoken.NoBridge;
        if (spoken.Stream == SpeechStream.Chatter)
        {
            memory.LastWasChatter = true;
            memory.LastChatterUtc = now;
        }
        else
        {
            memory.LastWasChatter = false;
            memory.LastCommandUtc = now;
        }
    }

    /// <summary>Optional reactions she may skip, depending on how talkative she is tonight.</summary>
    private static bool Wanted(Utterance u, NarrationContext c)
    {
        double talk = c.Persona.Talkativeness;
        return u.Act switch
        {
            SpeechAct.EdgeRemark => c.Random.Chance(12 + talk * 5),
            SpeechAct.SegmentStart => u.Has("first") || c.Random.Chance(35 + talk * 4),
            SpeechAct.PriceProgress => c.Random.Chance(50 + talk * 4),
            _ => true,
        };
    }

    private static bool OpensShort(string text)
    {
        int end = text.IndexOfAny(['.', '!', '?']);
        return end > 0 && text[..end].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2;
    }

    private static bool Optional(SpeechAct act) => act is SpeechAct.EdgeRemark or SpeechAct.PriceProgress or SpeechAct.SegmentStart;

    /// <summary>He may edge at any moment, and she'll have to answer: no time for a story.</summary>
    private static bool ExpectingEdge(SessionState s)
        => s.Current is { Kind: InstructionKind.EdgeCount } or { CountEdges: true } or { Category: StepCategory.Hold } || s.Price is not null;

    private static bool MayChat(SessionState s)
        => s is { Active: true, Paused: false, InterruptedAtUtc: null, FinishDecided: false, Game: null, Offer: null } && s.Current is not null;

    /// <summary>Seconds of quiet before the next command she knows about.</summary>
    private static int Quiet(SessionState s, DateTime now)
        => s.TimerDueUtc is DateTime due && !(s.Current?.HiddenTimer ?? false) ? Math.Max(0, (int)(due - now).TotalSeconds - CommandQuiet) : 90;

    private static bool Psych(SessionState s)
        => s.Current?.Segment is { } id && Sessions.Plan.SegmentCatalogue.Default.Find(id) is { } t && (t.Has("psych") || t.Has("rest"));

    /// <summary>Her usual gap between unprompted lines, from how talkative she is tonight.</summary>
    public static double Gap(SessionPersona persona, SessionState s)
    {
        double gap = 240 * Math.Pow(0.785, Math.Clamp(persona.Talkativeness, 0, 10));
        if (Psych(s)) gap *= 0.45;
        if (s.Mood.Mood is DommeMood.Cruel or DommeMood.Relentless) gap *= 1.3;
        return Math.Max(15, gap);
    }
}
