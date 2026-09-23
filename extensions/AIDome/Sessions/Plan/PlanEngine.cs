using AIDome.Speech;

namespace AIDome.Sessions.Plan;

/// <summary>What happened when he begged for a rest, as far as the plan is concerned.</summary>
public enum RestBegOutcome { NoRule, Granted, Denied }

/// <summary>
/// Makes the session plan and revises it. It works one level above segments: it chooses which
/// goal comes next from the properties each segment type declares (tags, intensity, what it likes
/// to follow, where it fits, what must follow it), the contract, her mood, the user's preferences
/// and her memory of earlier sessions. Events edit the remaining plan through a few general rules;
/// every change is logged.
/// </summary>
public sealed class PlanEngine(IRandom r, SegmentCatalogue catalogue)
{
    private const double Finished = 0.02;

    public SegmentCatalogue Catalogue => catalogue;

    // ── Making the plan ──

    public SessionPlan Make(SessionState s, Authorities a, IReadOnlyDictionary<string, SegmentPreference> preferences)
    {
        var plan = new SessionPlan { Preferences = new(preferences, StringComparer.OrdinalIgnoreCase) };
        s.Plan = plan;
        Fill(s, a, plan, 0, 1.0, null);
        if (!s.Contract.GamesOnly) Require(s, a, plan, DateTime.MinValue);
        if (!s.Contract.GamesOnly) plan.Segments.Add(new() { Type = SegmentCatalogue.Ending, Share = 0, MinSteps = 1, MaxSteps = 1 });
        return plan;
    }

    /// <summary>
    /// What the session asks for must be in the plan: a punishment session without anything that
    /// hurts isn't one. When the draw left it out, a planned segment in the second half gives way.
    /// </summary>
    private void Require(SessionState s, Authorities a, SessionPlan plan, DateTime now)
    {
        var traits = s.Contract.Obligations.Character;
        foreach (var (key, tags) in catalogue.Requires)
        {
            bool active = key switch
            {
                "punishment" => s.Contract.Punishment,
                "torturous" => traits.Torturous,
                "teasing" => traits.Teasing,
                "intense" => traits.Intense,
                "mean" => traits.Mean,
                _ => false,
            };
            if (!active || plan.Segments.Any(x => catalogue.Find(x.Type)?.HasAny(tags) == true)) continue;
            int half = plan.Segments.Count / 2;
            var slots = Enumerable.Range(half, plan.Segments.Count - half)
                .Where(i => plan.Segments[i] is { Origin: "plan" } seg && seg.Type != SegmentCatalogue.Ending && !Required(plan, seg))
                .ToList();
            if (slots.Count == 0) continue;
            int at = slots[r.Next(0, slots.Count)];
            var prev = at > 0 ? catalogue.Find(plan.Segments[at - 1].Type) : null;
            if (Pick(s, a, plan, prev, 0.7, tags) is not { } type) continue;
            string was = plan.Segments[at].Type;
            plan.Segments[at] = Plan(s, plan, type, plan.Segments[at].Share, "plan");
            plan.Revisions.Add($"planned {type.Id} instead of {was}: a {key} session needs it");
        }
    }

    /// <summary>Appends segments for <paramref name="share"/> of the budget, starting at <paramref name="progress"/>.</summary>
    private void Fill(SessionState s, Authorities a, SessionPlan plan, double progress, double share, string? origin, int? at = null)
    {
        int insertAt = at ?? plan.Segments.Count;
        SegmentType? prev = insertAt > 0 ? catalogue.Find(plan.Segments[insertAt - 1].Type) : null;
        double remaining = share;
        for (int guard = 0; guard < 40 && (remaining > Finished || prev?.FollowUp.Length > 0); guard++)
        {
            double p = Math.Clamp(progress + (share - remaining), 0, 1);
            var type = Pick(s, a, plan, prev, p, [], room: remaining);
            if (type is null) break;
            var segment = Plan(s, plan, type, remaining, origin);
            plan.Segments.Insert(insertAt++, segment);
            remaining = Math.Max(0, remaining - segment.Share);
            prev = type;
        }
    }

    private PlannedSegment Plan(SessionState s, SessionPlan plan, SegmentType type, double remaining, string? origin)
    {
        var preference = plan.Preferences.TryGetValue(type.Id, out var p) ? p : new();
        double lengthFactor = preference.Length switch { SegmentLength.Shorter => 0.65, SegmentLength.Longer => 1.5, _ => 1.0 };
        if (type.Has("torture")) lengthFactor *= 1 + s.Mood.Annoyance * 0.4;
        double share = Math.Max(Between(type.Share[0], type.Share[1]) * lengthFactor, Floor(s, type));
        if (remaining > 0 && remaining - share < 0.04) share = Math.Max(share, remaining);
        int maxSteps = (int)Math.Ceiling(type.Steps[1] * (preference.Length == SegmentLength.Longer ? 1.5 : 1));
        return new() { Type = type.Id, Share = Math.Round(Math.Max(0.01, share), 3), MinSteps = type.Steps[0], MaxSteps = Math.Max(type.Steps[0], maxSteps), Origin = origin ?? "plan" };
    }

    /// <summary>The weighted choice of the next segment type. Null when nothing fits.</summary>
    public SegmentType? Pick(SessionState s, Authorities a, SessionPlan plan, SegmentType? prev, double progress, IReadOnlyCollection<string> wantTags,
        double tortureBias = 1, double room = 1, IReadOnlyCollection<string>? exclude = null)
    {
        var candidates = new List<(SegmentType, double)>();
        foreach (var t in catalogue.Types)
        {
            if (!Eligible(s, a, plan, t)) continue;
            if (wantTags.Count > 0 && !t.HasAny(wantTags)) continue;
            if (exclude?.Contains(t.Id, StringComparer.OrdinalIgnoreCase) == true) continue;
            double w = Weight(s, plan, t, prev, progress);
            if (t.Has("torture") || t.Has("punish")) w *= tortureBias;
            // Something that can't even do its minimum in the time left fits badly.
            if (room < 1 && Floor(s, t) > room * 1.3) w *= 0.25;
            if (w > 0) candidates.Add((t, w));
        }
        return candidates.Count == 0 ? null : r.Weighted(candidates, candidates[0].Item1);
    }

    public bool Eligible(SessionState s, Authorities a, SessionPlan plan, SegmentType t, bool planned = true)
    {
        if (t.Id == SegmentCatalogue.Ending || planned && !t.Planned) return false;
        if (s.Contract.GamesOnly && !t.Has("game")) return false;
        foreach (var need in t.Needs)
        {
            bool held = need switch
            {
                "pacing" => a.Pacing.DommeSets,
                "edges" => a.Edges.DommeSets,
                "methods" => a.Methods.DommeChooses,
                _ => a.Games.MayPlay(s),
            };
            if (!held) return false;
        }
        if (plan.Preferences.TryGetValue(t.Id, out var p) && p.Frequency == Frequency.Never) return false;
        var persona = s.Persona;
        foreach (var (dial, min) in t.Persona)
        {
            int value = dial switch
            {
                "harsh" => persona.Harshness, "humiliation" => persona.Humiliation, "manipulation" => persona.Manipulation, _ => persona.Talkativeness,
            };
            if (value < min) return false;
        }
        return true;
    }

    private double Weight(SessionState s, SessionPlan plan, SegmentType t, SegmentType? prev, double progress)
    {
        var preference = plan.Preferences.TryGetValue(t.Id, out var p) ? p : new();
        double w = preference.Frequency switch { Frequency.Rarely => 0.35, Frequency.Often => 2.2, _ => 1.0 };

        string position = progress < 0.15 ? "start" : progress > 0.7 ? "late" : "middle";
        w *= t.Position.TryGetValue(position, out var pw) ? pw : 1;

        if (prev is not null)
        {
            if (prev.FollowUp.Length > 0 && !t.HasAny(prev.FollowUp)) return 0;
            double follows = 1;
            foreach (var tag in prev.Tags)
                if (t.Follows.TryGetValue(tag, out var f)) follows *= f;
            w *= Math.Clamp(follows, 0.05, 6);
            if (prev.Id == t.Id && !t.Repeatable) w *= 0.03;
        }

        int uses = plan.Segments.Count(x => x.Type == t.Id);
        w /= 1 + uses * 0.7;

        // The escalation curve: a rise across the session, shaped by intensity, with dips coming from
        // what each type likes to follow.
        double intensity = s.Intensity;
        double low = 1.5 + intensity * 0.25, peak = 3.5 + intensity * 0.6 + (s.Contract.Punishment ? 1.2 : 0);
        double target = low + (peak - low) * Math.Pow(progress, 1.2);
        double miss = t.Intensity - target;
        w *= Math.Max(0.08, Math.Exp(-(miss * miss) / (2 * 2.2 * 2.2)));

        if (catalogue.MoodLenses.TryGetValue(s.Mood.Mood.ToString(), out var lens))
            foreach (var tag in t.Tags)
                if (lens.TryGetValue(tag, out var m)) w *= 1 + (m - 1) * Math.Clamp(s.Mood.Intensity * 1.4, 0.3, 1.2);

        var traits = s.Contract.Obligations.Character;
        if (traits.Torturous && t.Has("torture")) w *= 2;
        if (traits.Teasing && t.Has("tease")) w *= 2;
        if (traits.Intense && t.Intensity >= 6) w *= 1.6;
        if (traits.Mean && (t.Has("torture") || t.Has("punish"))) w *= 1.3;
        if (s.Contract.Punishment)
        {
            if (t.Has("torture")) w *= 1.7;
            if (t.Has("rest")) w *= 0.5;
            if (t.Has("tease")) w *= 0.6;
        }
        if (t.Has("torture")) w *= 1 + s.Mood.Annoyance;

        // Not the same as last time.
        var recall = s.Recall;
        if (progress < 0.05 && recall.RecentOpenings.Take(2).Contains(t.Id, StringComparer.OrdinalIgnoreCase)) w *= 0.25;
        if (recall.LastSegments.Contains(t.Id, StringComparer.OrdinalIgnoreCase)) w *= 0.8;
        return w;
    }

    // ── Running it ──

    /// <summary>Whether the current segment has done what it was for.</summary>
    public bool Done(SessionState s)
    {
        var seg = s.Plan?.Current;
        if (seg is null || seg.Type == SegmentCatalogue.Ending || !seg.Started) return false;
        if (seg.Steps >= seg.MaxSteps) return true;
        if (seg.Steps < seg.MinSteps) return false;
        // Done when another step would overshoot by more than it falls short now.
        double need = seg.Share * s.Budget - (s.Points - seg.PointsAtStart);
        double step = catalogue.Find(seg.Type) is { } type ? StepPoints(s, type) : 0;
        return need <= step * 0.5;
    }

    /// <summary>
    /// Moves to the next segment, applying the rules that act at segment boundaries: keeping the plan
    /// in step with the points clock, her annoyance, an occasional change of mind, and foreshadowing
    /// the ending. Returns what she says as the new segment starts.
    /// </summary>
    public List<Utterance> Next(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan!;
        var say = new List<Utterance>();
        bool movedOn = plan.Current is { Started: true };
        if (movedOn) plan.Index++;

        // The points are the session's clock in every mode: the plan bends to them. Only when a
        // segment has finished; a segment just inserted (the ending waits) is never dropped.
        double progress = s.Budget <= 0 ? 0 : s.Points / s.Budget;
        if (movedOn && !plan.AtEnding && !s.Contract.GamesOnly) Rebalance(s, plan, progress, now);
        if ((plan.AtEnding && progress < 0.85) || plan.Current is null)
        {
            Fill(s, a, plan, progress, Math.Max(0.1, 1 - progress), "extended", plan.Index);
            Log(s, now, $"extended the session at {progress:P0}");
        }

        // Annoyed: what comes next is harder; and sometimes she simply changes her mind.
        if (plan.Current is { } next && !plan.AtEnding && catalogue.Find(next.Type) is { } nextType)
        {
            var prev = plan.Index > 0 ? catalogue.Find(plan.Segments[plan.Index - 1].Type) : null;
            // A replacement is never what was planned, nor what comes straight after it.
            string[] exclude = [next.Type, .. plan.Segments.Skip(plan.Index + 1).Take(1).Select(x => x.Type)];
            if (s.Mood.Annoyance >= 0.55 && !nextType.Has("torture") && !nextType.Has("punish") && r.Chance(s.Mood.Annoyance * 100)
                && Pick(s, a, plan, prev, progress, [], tortureBias: 4, exclude: exclude) is { } harder)
            {
                Replace(s, plan, harder, "annoyed", now, $"annoyed ({s.Mood.Cause}): {next.Type} became {harder.Id}");
            }
            else if (r.Chance(4 + s.Persona.Manipulation * 0.4) && Pick(s, a, plan, prev, progress, [], exclude: exclude) is { } other)
            {
                Replace(s, plan, other, "changed her mind", now, $"changed her mind: {next.Type} became {other.Id}");
            }
        }

        if (plan.Current is { } current)
        {
            current.Started = true;
            current.PointsAtStart = s.Points;
            current.Steps = 0;
            var type = catalogue.Find(current.Type);
            var start = Utterance.Of(SpeechAct.SegmentStart, $"segment:{current.Type}", $"origin:{Slug(current.Origin)}")
                .Tagged((type?.Tags ?? []).Select(x => $"tag:{x}").ToArray());
            if (plan.Index == 0) start = start.Tagged("first");
            say.Add(start);
        }

        // Past the middle, she may hint at the ending: the truth, or a lie.
        if (!plan.AtEnding && s.Ending.Presentation != Presentation.Silent && plan.Foreshadowed < (s.Length >= 7 ? 2 : 1)
            && progress >= 0.45 && r.Chance(45))
        {
            EndingAuthority.Refresh(s, s.Ending, projected: true);
            plan.Foreshadowed++;
            say.Add(Utterance.Of(SpeechAct.Foreshadow, $"lean:{s.Ending.Kind}", $"presentation:{s.Ending.Presentation}") with { Stream = SpeechStream.Chatter });
        }
        return say;
    }

    /// <summary>
    /// Keeps what is left of the plan in step with the points clock. Running a little long, the
    /// remaining segments shrink; running very long, she drops the planned segments that fit late
    /// in a session least, keeping what she inserted for a reason and anything a segment requires.
    /// </summary>
    private void Rebalance(SessionState s, SessionPlan plan, double progress, DateTime now)
    {
        double left = Math.Max(0, 1 - progress);
        List<PlannedSegment> rest() => plan.Segments.Skip(plan.Index).Where(x => x.Type != SegmentCatalogue.Ending).ToList();
        double planned = rest().Sum(x => x.Share);
        if (planned <= left * 1.15 + 0.02) return;

        var dropped = new List<string>();
        while (planned > left * 1.6 + 0.02)
        {
            var droppable = rest().Where(x => x.Origin is "plan" or "extended" && !Required(plan, x)).ToList();
            if (droppable.Count == 0) break;
            var drop = droppable.MinBy(x => (catalogue.Find(x.Type)?.Position.GetValueOrDefault("late", 1) ?? 1) * (0.8 + r.NextDouble() * 0.4))!;
            plan.Segments.Remove(drop);
            dropped.Add(drop.Type);
            planned = rest().Sum(x => x.Share);
        }
        if (planned > left * 1.15 + 0.02 && planned > 0)
        {
            double factor = Math.Max(0.35, left / planned);
            foreach (var seg in rest()) seg.Share = Math.Round(Math.Max(0.01, seg.Share * factor), 3);
        }
        Log(s, now, $"running long at {progress:P0}: " + (dropped.Count > 0 ? $"dropped {string.Join(", ", dropped)}, " : "") + "shortened the rest");
    }

    /// <summary>Whether the segment before this one requires it (its followUp).</summary>
    private bool Required(SessionPlan plan, PlannedSegment seg)
    {
        int i = plan.Segments.IndexOf(seg);
        return i > 0 && catalogue.Find(plan.Segments[i - 1].Type) is { FollowUp.Length: > 0 } before
            && catalogue.Find(seg.Type) is { } type && type.HasAny(before.FollowUp);
    }

    private void Replace(SessionState s, SessionPlan plan, SegmentType type, string origin, DateTime now, string log)
    {
        var old = plan.Segments[plan.Index];
        var replacement = Plan(s, plan, type, old.Share, origin);
        replacement.Share = Math.Max(replacement.Share, old.Share * 0.8);
        plan.Segments[plan.Index] = replacement;
        Log(s, now, log);
    }

    /// <summary>Adds a segment before the ending: the session runs a little longer.</summary>
    public void Extend(SessionState s, Authorities a, DateTime now, double share, string why)
    {
        var plan = s.Plan;
        if (plan is null) return;
        int at = Math.Min(plan.Index, plan.Segments.Count);
        var prev = at > 0 ? catalogue.Find(plan.Segments[at - 1].Type) : null;
        if (Pick(s, a, plan, prev, 0.95, []) is not { } type) return;
        plan.Segments.Insert(at, Resized(Plan(s, plan, type, share, why), share));
        Log(s, now, $"{why}: added {type.Id}");
    }

    // ── Events ──

    /// <summary>
    /// He begged for a rest. A segment type may say what she does then: grant it, and perhaps swap
    /// to something else before coming back ("mercy with a twist").
    /// </summary>
    public RestBegOutcome OnRestBeg(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan?.Current is not { } seg || catalogue.Find(seg.Type) is not { OnRestBeg: { } rule } type) return RestBegOutcome.NoRule;
        if (!r.Chance(rule.Grant * 100 * (1 - s.Mood.Annoyance * 0.5))) return RestBegOutcome.Denied;
        double done = s.Budget <= 0 ? 0 : (s.Points - seg.PointsAtStart) / s.Budget;
        double left = Math.Max(0, seg.Share - done);
        seg.MaxSteps = seg.Steps; // this segment stops here
        int at = plan.Index + 1;
        var parts = new List<string>();
        if (rule.SwapTo.Length > 0 && Pick(s, a, plan, type, s.Progress, rule.SwapTo) is { } swap)
        {
            var twist = Plan(s, plan, swap, 0.08, "rest beg");
            twist.Share = Math.Min(twist.Share, 0.1);
            plan.Segments.Insert(at++, twist);
            parts.Add($"then {swap.Id}");
        }
        if (rule.Resume && left > 0.03)
        {
            plan.Segments.Insert(at, Resized(Plan(s, plan, type, left, "resumed after a rest beg"), left));
            parts.Add($"then back to {type.Id}");
        }
        Log(s, now, $"granted a rest beg during {type.Id}" + (parts.Count > 0 ? $": a rest, {string.Join(", ", parts)}" : ""));
        return RestBegOutcome.Granted;
    }

    /// <summary>He keeps asking to cum: the ending moves further away.</summary>
    public void OnPestering(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan is null || s.RequestsDenied < 3 || !r.Chance(50)) return;
        int ending = plan.Segments.FindIndex(x => x.Type == SegmentCatalogue.Ending);
        if (ending < 0 || ending <= plan.Index) return;
        var prev = catalogue.Find(plan.Segments[ending - 1].Type);
        if (Pick(s, a, plan, prev, 0.9, ["edging", "torture"]) is not { } type) return;
        plan.Segments.Insert(ending, Resized(Plan(s, plan, type, 0.08, "pestering"), 0.08));
        Log(s, now, $"he kept asking: added {type.Id} before the ending");
    }

    /// <summary>He failed a challenge: a punishment comes next.</summary>
    public void OnChallengeFailed(SessionState s, Authorities a, DateTime now)
    {
        // The challenge is over the moment he fails it.
        if (s.Plan?.Current is { } seg) seg.MaxSteps = seg.Steps;
        Punish(s, a, now, 0.06, "failed a challenge", replaceRest: false);
    }

    /// <summary>An orgasm without permission: the rest of the plan becomes punishment.</summary>
    public void OnUnauthorizedOrgasm(SessionState s, Authorities a, DateTime now) => Punish(s, a, now, 0.15, "came without permission", replaceRest: true);

    private void Punish(SessionState s, Authorities a, DateTime now, double share, string why, bool replaceRest)
    {
        var plan = s.Plan;
        if (plan is null) return;
        if (replaceRest)
            plan.Segments.RemoveAll(x => plan.Segments.IndexOf(x) > plan.Index && x.Type != SegmentCatalogue.Ending);
        var punish = catalogue.Types.Where(t => t.Has("punish") && Eligible(s, a, plan, t, planned: false)).ToList();
        if (punish.Count == 0) punish = catalogue.Types.Where(t => t.Has("torture") && Eligible(s, a, plan, t)).ToList();
        if (punish.Count == 0) return;
        var type = r.Pick(punish);
        plan.Segments.Insert(Math.Min(plan.Index + 1, plan.Segments.Count), Resized(Plan(s, plan, type, share, why), share));
        Log(s, now, $"{why}: inserted {type.Id}" + (replaceRest ? " and dropped the rest of the plan" : ""));
    }

    /// <summary>The contract changed mid-session: the rest of the plan is made again.</summary>
    public void OnContractChanged(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan is null) return;
        plan.Segments.RemoveAll(x => plan.Segments.IndexOf(x) > plan.Index && x.Type != SegmentCatalogue.Ending);
        if (plan.Current is { } seg && catalogue.Find(seg.Type) is { } type && !Eligible(s, a, plan, type, planned: false)) seg.MaxSteps = seg.Steps;
        Fill(s, a, plan, s.Progress, Math.Max(0.1, 1 - s.Progress), "settings changed", plan.Index + 1);
        Log(s, now, "settings changed: planned the rest again");
    }

    /// <summary>The smallest share of the budget this type can take: its minimum steps, costed.</summary>
    private static double Floor(SessionState s, SegmentType t) => s.Budget <= 0 ? 0 : t.Steps[0] * StepPoints(s, t) / s.Budget;

    /// <summary>
    /// Roughly what one step of this type earns in points on his methods, by the same rules that
    /// score it. Rests earn nothing; a game is guessed.
    /// </summary>
    public static double StepPoints(SessionState s, SegmentType t)
    {
        var avg = MethodInfo.UserChoice(s.Methods);
        var st = t.Step;
        static double Mid(int[] a) => a.Length == 0 ? 0 : (a[0] + a[^1]) / 2.0;
        int speed = (int)Math.Round(Mid(st.Speed));
        double Time(double seconds) => Points.ForStimulation(avg, seconds, speed);
        const double ToEdge = 35; // seconds of stimulation to reach an edge, on average
        double edge = avg.EdgePoints + Time(ToEdge);
        return st.Kind switch
        {
            StepKind.Rest or StepKind.TeaseRest => 0,
            StepKind.Game => 5,
            StepKind.Edges => Mid(st.Count) * edge + st.HoldChance * Points.ForHold((int)Mid(st.Hold)),
            StepKind.Hold => edge + Points.ForHold((int)Mid(st.Hold)),
            StepKind.MaxEdges => Time(Mid(st.Seconds)) + Mid(st.Seconds) / 30 * avg.EdgePoints,
            _ => Time(Mid(st.Seconds)),
        };
    }

    private static PlannedSegment Resized(PlannedSegment seg, double share)
    {
        seg.Share = Math.Round(Math.Max(0.01, share), 3);
        return seg;
    }

    private static void Log(SessionState s, DateTime now, string what)
    {
        var plan = s.Plan!;
        plan.Revisions.Add($"{(now - s.StartedAtUtc).TotalMinutes:0.0} min, step {s.StepNumber}: {what}");
        if (plan.Revisions.Count > 60) plan.Revisions.RemoveAt(0);
    }

    private static string Slug(string origin) => origin.Replace(' ', '-');

    private double Between(double lo, double hi) => lo + r.NextDouble() * (hi - lo);
}
