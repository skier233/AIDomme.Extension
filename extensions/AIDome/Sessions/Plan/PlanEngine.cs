using AIDome.Speech;

namespace AIDome.Sessions.Plan;

/// <summary>What happened when he begged for a rest, as far as the plan is concerned.</summary>
public enum RestBegOutcome { NoRule, Granted, Denied }

/// <summary>
/// Her scheduling. She sets aims for the night (a purpose plus what she wants achieved), and inside
/// each aim chooses one segment at a time from his state: more of the aim, something to support it
/// (a break, a game with the aim's stakes), a side of something else, or a written segment when
/// one is allowed, fits, is fresh and its turn comes up. Events change the aims. Every choice
/// records a one-line reason, and nothing here makes steps: the steerer does that.
/// </summary>
public sealed class PlanEngine(IRandom r, SegmentCatalogue catalogue)
{
    public SegmentCatalogue Catalogue => catalogue;

    /// <summary>Roughly how many minutes one aim takes; a session has as many aims as fit.</summary>
    private const double AimMinutes = 8;
    private const int MaxAims = 8;

    // ── Making the plan ──

    public SessionPlan Make(SessionState s, Authorities a)
    {
        var plan = new SessionPlan();
        s.Plan = plan;
        double minutes = Minutes(s);
        int count = s.Contract.GamesOnly ? 1 : Math.Clamp((int)Math.Round(minutes / AimMinutes), 1, MaxAims);
        Purpose? previous = null;
        for (int i = 0; i < count; i++)
        {
            double progress = i / (double)count;
            // His state at that point is a guess: need climbs through a session.
            var projected = new BodyState { Need = Math.Min(0.8, 0.05 + progress * 0.7), Control = 1, Closeness = progress > 0 ? 0.6 : 0 };
            var (purpose, why) = Choose(s, a, plan, projected, progress, previous);
            plan.Aims.Add(NewAim(s, a, plan, purpose, Size(purpose), why, "plan", projected));
            previous = purpose;
        }
        Normalise(plan);
        if (!s.Contract.GamesOnly) Require(s, a, plan);
        return plan;
    }

    /// <summary>How big an aim is next to the others: starving him and overwhelming him are shorter than the long work of edging.</summary>
    private double Size(Purpose p) => (0.75 + r.NextDouble() * 0.5) * Factor(p);
    private static double Factor(Purpose p) => p switch { Purpose.Starve => 0.5, Purpose.Overwhelm => 0.7, _ => 1 };

    /// <summary>The aims not yet begun share what is left of the session.</summary>
    private static void Normalise(SessionPlan plan)
    {
        double begun = plan.Aims.Where(x => x.Started).Sum(x => x.Share);
        var open = plan.Aims.Where(x => !x.Started && x.Origin == "plan").ToList();
        double total = open.Sum(x => x.Share);
        if (total <= 0) return;
        foreach (var aim in open) aim.Share = Math.Round(aim.Share / total * Math.Max(0.05, 1 - begun), 3);
    }

    private static double Minutes(SessionState s) => s.Budget / Math.Max(0.5, Points.Pace(s.Recall));

    /// <summary>
    /// What the session asks for must be in the plan: a punishment session without overwhelm isn't
    /// one. When the draw left it out, an aim in the second half gives way.
    /// </summary>
    private void Require(SessionState s, Authorities a, SessionPlan plan)
    {
        var traits = s.Contract.Obligations.Character;
        foreach (var (key, purposes) in catalogue.Requires)
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
            if (!active || plan.Aims.Any(x => purposes.Contains(x.Purpose))) continue;
            var wanted = purposes.Where(p => Eligible(s, a, p)).ToList();
            if (wanted.Count == 0) continue;
            int at = plan.Aims.Count == 1 ? 0 : r.Next(plan.Aims.Count / 2, plan.Aims.Count);
            var was = plan.Aims[at];
            plan.Aims[at] = NewAim(s, a, plan, wanted[0], was.Share, $"a {key} session needs it", "plan", new BodyState { Need = 0.4 });
            Normalise(plan);
            plan.Revisions.Add($"planned {Purposes.Name(wanted[0])} instead of {Purposes.Name(was.Purpose)}: a {key} session needs it");
        }
    }

    private Aim NewAim(SessionState s, Authorities a, SessionPlan plan, Purpose purpose, double share, string why, string origin, BodyState b)
    {
        var aim = new Aim { Purpose = purpose, Share = Math.Round(share, 3), Why = why, Origin = origin };
        switch (purpose)
        {
            case Purpose.Ache:
                aim.Target = Math.Round(Math.Min(0.85, b.Need + 0.3), 2);
                aim.Goal = $"his need at {aim.Target:P0} or more";
                break;
            case Purpose.Control:
                aim.Target = s.Mean >= 7 ? 0.2 : 0.3;
                aim.Goal = $"his control worn down to {aim.Target:P0}, without a slip";
                break;
            case Purpose.Overwhelm:
                aim.Target = 0.8;
                aim.Goal = "overwhelmed (80% or more) and kept there";
                break;
            default:
                aim.Goal = "kept wanting, with next to nothing, for as long as it lasts";
                break;
        }
        // A written shape, when one is allowed, fresh, and its turn comes up.
        if (!s.Contract.GamesOnly && origin is "plan" or "extended")
            foreach (var written in catalogue.Aims.Where(x => x.Purpose == purpose && x.Event.Length == 0))
            {
                if (!Allowed(s, a, written.Needs, written.Persona) || plan.Aims.Any(x => x.Written == written.Id)) continue;
                if (!r.Chance(written.Chance * 100)) continue;
                aim.Written = written.Id;
                aim.Goal = written.Goal;
                aim.Why += $"; as {written.Name.ToLowerInvariant()}";
                break;
            }
        return aim;
    }

    // ── Choosing a purpose from his state ──

    /// <summary>Whether she can pursue a purpose at all with what she controls in this session.</summary>
    public bool Eligible(SessionState s, Authorities a, Purpose p) => p switch
    {
        Purpose.Ache => a.Pacing.DommeSets || a.Edges.DommeSets,
        Purpose.Control => a.Edges.DommeSets,
        Purpose.Overwhelm => a.Pacing.DommeSets && (!a.Methods.DommeChooses || s.Methods.Any(m => m.TortLevel >= 5)),
        _ => a.Pacing.DommeSets,
    };

    /// <summary>How much she wants each purpose now, and why. His state leads; her mood and persona lean on it.</summary>
    public List<(Purpose Purpose, double Weight, string Why)> Weigh(SessionState s, Authorities a, SessionPlan plan, BodyState b, double progress, Purpose? previous)
    {
        var traits = s.Contract.Obligations.Character;
        var persona = s.Persona;
        double harsh = persona.Harshness / 10.0, mean = s.Mean / 10.0;
        var list = new List<(Purpose, double, string)>();
        foreach (var p in new[] { Purpose.Ache, Purpose.Control, Purpose.Overwhelm, Purpose.Starve })
        {
            if (!Eligible(s, a, p)) continue;
            double w;
            string why;
            switch (p)
            {
                case Purpose.Ache:
                    w = 1.5 - b.Need;
                    if (progress < 0.15) w *= 2;
                    why = b.Need < 0.35 ? $"his need is low ({b.Need:P0}): build the ache first" : $"need at {b.Need:P0}: keep him wanting";
                    break;
                case Purpose.Control:
                    w = (0.6 + 1.6 * b.Need) * (0.3 + 0.7 * b.Control);
                    if (progress < 0.1) w *= 0.4;
                    if (s.Intensity <= 3) w *= 0.7; else if (s.Intensity >= 8) w *= 1.3;
                    if (traits.Intense) w *= 1.6;
                    why = $"need at {b.Need:P0} and control at {b.Control:P0}: see how much he can take at the edge";
                    break;
                case Purpose.Overwhelm:
                    w = (0.15 + harsh * 0.5 + mean * 0.5) * (1 - 0.8 * b.Overwhelm) * (1 + s.Mood.Annoyance * 1.5);
                    if (progress < 0.12) w *= progress <= 0 ? 0.02 : 0.1;
                    if (s.Intensity <= 3) w *= 0.4; else if (s.Intensity >= 8) w *= 1.3;
                    if (persona.Harshness < 3) w *= 0.3;
                    if (s.Contract.Punishment) w *= 2.2;
                    if (traits.Torturous) w *= 2;
                    if (traits.Mean) w *= 1.3;
                    why = s.Contract.Punishment ? "a punishment: stimulation he would rather stop"
                        : s.Mood.Annoyance >= 0.4 ? $"she is annoyed ({s.Mood.Cause}): stimulation he would rather stop"
                        : $"overwhelm at {b.Overwhelm:P0}: time for stimulation he would rather stop";
                    break;
                default:
                    w = 0.45 + 0.5 * b.Overwhelm + (b.CurrentAbove80 >= 240 ? 0.6 : 0);
                    if (progress < 0.1) w *= progress <= 0 ? 0.03 : 0.5;
                    // The night rarely ends on nothing.
                    if (progress >= 0.7) w *= 0.3;
                    if (s.Contract.Punishment) w *= 0.6;
                    if (traits.Teasing) w *= 2;
                    why = b.Overwhelm >= 0.5 ? $"overwhelm at {b.Overwhelm:P0}: take it all away and make him want it back"
                        : b.CurrentAbove80 >= 240 ? "a long stretch near the edge: drop him to almost nothing"
                        : $"need at {b.Need:P0}: give him almost nothing and let it gnaw";
                    break;
            }
            if (p == Purpose.Ache && traits.Teasing) w *= 1.4;
            double lens = catalogue.Lens(s.Mood.Mood, Purposes.Id(p));
            w *= 1 + (lens - 1) * Math.Clamp(s.Mood.Intensity * 1.4, 0.3, 1.2);
            if (lens >= 1.3) why += $"; she is {s.Mood.Mood.ToString().ToLowerInvariant()}";
            if (previous == p) w *= 0.2;
            w /= 1 + plan.Aims.Count(x => x.Purpose == p && x.Started) * 0.5;
            if (w > 0) list.Add((p, w, why));
        }
        return list;
    }

    private (Purpose Purpose, string Why) Choose(SessionState s, Authorities a, SessionPlan plan, BodyState b, double progress, Purpose? previous)
    {
        var options = Weigh(s, a, plan, b, progress, previous);
        if (options.Count == 0) return (Purpose.Ache, "nothing else is hers to decide");
        var purpose = r.Weighted(options.Select(x => (x.Purpose, x.Weight)).ToList(), options[0].Purpose);
        return (purpose, options.First(x => x.Purpose == purpose).Why);
    }

    // ── Running it ──

    /// <summary>Whether the current segment has done what it was for.</summary>
    public bool Done(SessionState s, DateTime now)
    {
        var plan = s.Plan;
        var seg = plan?.Current;
        if (seg is null || seg.Type == SegmentCatalogue.Ending || !seg.Started) return false;
        if (seg.Stop) return End(seg, now, seg.Ended.Length > 0 ? seg.Ended : "cut short");
        if (seg.Written)
        {
            if (seg.Steps >= seg.MaxSteps) return End(seg, now, "ran its steps");
            if (seg.Steps < seg.MinSteps) return false;
            double need = seg.Share * s.Budget - (s.Points - seg.PointsAtStart);
            double step = catalogue.Find(seg.Type) is { } type ? StepPoints(s, type) : 0;
            return need <= step * 0.5 && End(seg, now, "ran its share");
        }
        if (seg.Steps < 1) return false;
        var b = s.Body;
        // He is close to going over: she eases off rather than push him into a slip.
        if (seg.Edges && seg.Purpose is Purpose.Control or Purpose.Ache && b.Control < 0.2) return End(seg, now, $"his control is thin ({b.Control:P0})");
        double elapsed = seg.StartedUtc is DateTime since ? (now - since).TotalSeconds : 0;
        if (elapsed >= seg.Seconds) return End(seg, now, "ran its time");
        // The aim it serves is over: the segment has no reason to go on.
        if (seg.Role == SegmentRole.Advancing && plan!.CurrentAim is { } aim && AimDone(s, aim, now) is { } why) return End(seg, now, $"the aim ended ({why})");
        return false;
    }

    private static bool End(PlannedSegment seg, DateTime now, string why)
    {
        seg.Ended = why;
        seg.EndedUtc = now;
        return true;
    }

    /// <summary>Why the aim is over, or null while it runs: what she wanted is achieved, or its share or its time is spent.</summary>
    private string? AimDone(SessionState s, Aim aim, DateTime now)
    {
        if (!aim.Started) return null;
        if (s.Contract.GamesOnly) return null;
        double share = aim.Share * s.Budget;
        double earned = s.Points - aim.PointsAtStart;
        var b = s.Body;
        bool reached = aim.Purpose switch
        {
            Purpose.Ache => b.Need >= aim.Target,
            Purpose.Control => b.Control <= aim.Target,
            Purpose.Overwhelm => b.Overwhelm >= aim.Target,
            _ => false,
        };
        if (reached && aim.Target > 0) aim.Achieved = true;
        if (aim.Achieved && earned >= share * 0.6) return "she got what she wanted";
        if (earned >= share) return "it ran its share of the session";
        double elapsed = aim.StartedUtc is DateTime since ? (now - since).TotalSeconds : 0;
        // Next to nothing earns next to nothing, so starving him runs on the clock, and not for long.
        if (elapsed >= aim.Share * Minutes(s) * 60 * (aim.Purpose == Purpose.Starve ? 0.8 : 1.3)) return "its time was up";
        return null;
    }

    /// <summary>
    /// Moves to the next segment: closes the aim if it is over, starts the next (revised to his
    /// state as it is now), and chooses what comes next inside it. Returns what she says as it starts.
    /// </summary>
    public List<Utterance> Next(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan!;
        var say = new List<Utterance>();
        var before = plan.Current is { Started: true } ? plan.Current : null;
        if (before is not null)
        {
            if (before.EndedUtc is null) End(before, now, "moved on");
            plan.Index++;
        }
        // An ending queued before more aims were added waits its turn.
        if (plan.Current is { Type: SegmentCatalogue.Ending } && plan.CurrentAim is not null) plan.Segments.RemoveAt(plan.Index);

        bool aimStarted = false;
        if (plan.Current is null)
        {
            // Nothing queued: is the aim over, and what does she want next?
            if (plan.CurrentAim is { Started: true } aim && AimDone(s, aim, now) is { } why) CloseAim(s, plan, aim, now, why);
            if (plan.CurrentAim is null)
            {
                double progress = s.Progress;
                if (s.Contract.GamesOnly || progress < 0.85)
                {
                    var (purpose, reason) = Choose(s, a, plan, s.Body, progress, plan.Aims.LastOrDefault()?.Purpose);
                    plan.Aims.Add(NewAim(s, a, plan, purpose, Math.Max(0.1, 1 - progress), reason, "extended", s.Body));
                    Log(s, now, $"extended the session at {progress:P0}: {Purposes.Name(purpose)}");
                }
                else plan.Segments.Add(new() { Type = SegmentCatalogue.Ending, Written = true, Purpose = Purpose.Cool, MinSteps = 1, MaxSteps = 1, Reason = "every aim is done" });
            }
            if (plan.CurrentAim is { Started: false } next)
            {
                Reconsider(s, a, plan, next, now);
                next = plan.CurrentAim!;
                next.Started = true;
                next.StartedUtc = now;
                next.PointsAtStart = s.Points;
                aimStarted = true;
            }
            if (plan.Current is null && plan.CurrentAim is { } current) plan.Segments.Add(ChooseSegment(s, a, plan, current, now));
        }

        if (plan.Current is { } seg)
        {
            seg.Started = true;
            seg.StartedUtc = now;
            seg.PointsAtStart = s.Points;
            seg.Steps = 0;
            if (seg.Type != SegmentCatalogue.Ending)
            {
                if (seg.Aim < 0) seg.Aim = Math.Min(plan.AimIndex, plan.Aims.Count - 1);
                if (seg.Aim >= 0) plan.Aims[seg.Aim].Segments++;
            }
            var type = catalogue.Find(seg.Type);
            var start = Utterance.Of(SpeechAct.SegmentStart, $"segment:{seg.Type}", $"origin:{Slug(seg.Origin)}", $"purpose:{Purposes.Id(seg.Purpose)}", $"role:{seg.Role.ToString().ToLowerInvariant()}")
                .Tagged((type?.Tags ?? []).Select(x => $"tag:{x}").ToArray());
            if (plan.Index == 0) start = start.Tagged("first");
            // She does not always say what she is after: sometimes she tells him, mostly she just does it.
            if (aimStarted && plan.AimIndex > 0 && r.Chance(30)) start = start.Tagged("aim-start");
            // Back to what she was doing after a break, a game or a side of something else.
            if (!aimStarted && seg.Role == SegmentRole.Advancing && before is { Role: not SegmentRole.Advancing } && r.Chance(50)) start = start.Tagged("back");
            if (seg.Announce > 0) start = start.With("count", seg.Announce);
            // More of the same inside one aim needs no announcement.
            bool same = before is not null && before.Type == seg.Type && before.Aim == seg.Aim && !aimStarted && !seg.Written;
            if (!same) say.Add(start);
        }

        // Past the middle, she may hint at the ending: the truth, or a lie.
        if (!plan.AtEnding && s.Ending.Presentation != Presentation.Silent && plan.Foreshadowed < (s.Length >= 7 ? 2 : 1)
            && s.Progress >= 0.45 && r.Chance(45))
        {
            EndingAuthority.Refresh(s, s.Ending, projected: true);
            plan.Foreshadowed++;
            // Remember what the hint says, so the ending can own it or reveal the lie.
            bool allowed = s.Ending.Kind is EndingKind.Full or EndingKind.Ruined;
            if (s.Ending.Kind != EndingKind.PunishmentEnding)
                s.Ending.HintedOrgasm = s.Ending.Presentation == Presentation.Honest ? allowed : !allowed;
            say.Add(Utterance.Of(SpeechAct.Foreshadow, $"lean:{s.Ending.Kind}", $"presentation:{s.Ending.Presentation}") with { Stream = SpeechStream.Chatter });
        }
        return say;
    }

    private void CloseAim(SessionState s, SessionPlan plan, Aim aim, DateTime now, string why)
    {
        aim.Ended = why;
        aim.EndedUtc = now;
        plan.AimIndex++;
    }

    /// <summary>
    /// The aim she planned was a guess at his state. As it begins she looks at him as he is: when
    /// what she planned now fits badly and something else fits well, she changes her mind.
    /// </summary>
    private void Reconsider(SessionState s, Authorities a, SessionPlan plan, Aim aim, DateTime now)
    {
        if (aim.Origin != "plan" || s.Contract.GamesOnly) return;
        var previous = plan.AimIndex > 0 ? plan.Aims[plan.AimIndex - 1].Purpose : (Purpose?)null;
        var options = Weigh(s, a, plan, s.Body, s.Progress, previous);
        if (options.Count == 0) return;
        var best = options.MaxBy(x => x.Weight);
        double planned = options.Where(x => x.Purpose == aim.Purpose).Select(x => x.Weight).FirstOrDefault();
        if (best.Purpose == aim.Purpose || planned >= best.Weight * 0.3)
        {
            // Still what she wants: the reason is now about him as he is.
            if (options.FirstOrDefault(x => x.Purpose == aim.Purpose) is { Why: { } why } && aim.Written.Length == 0 && !aim.Why.Contains("needs it")) aim.Why = why;
            if (aim.Purpose == Purpose.Ache) { aim.Target = Math.Round(Math.Min(0.85, s.Body.Need + 0.3), 2); aim.Goal = $"his need at {aim.Target:P0} or more"; }
            return;
        }
        // What the session requires stays.
        if (RequiredPurposes(s).Contains(aim.Purpose) && plan.Aims.Count(x => x.Purpose == aim.Purpose) == 1) return;
        var replacement = NewAim(s, a, plan, best.Purpose, aim.Share * Factor(best.Purpose) / Factor(aim.Purpose), best.Why, "plan", s.Body);
        plan.Aims[plan.AimIndex] = replacement;
        Log(s, now, $"changed her aim from {Purposes.Name(aim.Purpose)} to {Purposes.Name(best.Purpose)}: {best.Why}");
    }

    private HashSet<Purpose> RequiredPurposes(SessionState s)
    {
        var traits = s.Contract.Obligations.Character;
        var set = new HashSet<Purpose>();
        foreach (var (key, purposes) in catalogue.Requires)
            if (key switch { "punishment" => s.Contract.Punishment, "torturous" => traits.Torturous, "teasing" => traits.Teasing, "intense" => traits.Intense, "mean" => traits.Mean, _ => false })
                foreach (var p in purposes) set.Add(p);
        return set;
    }

    // ── Choosing the next segment inside an aim ──

    private PlannedSegment ChooseSegment(SessionState s, Authorities a, SessionPlan plan, Aim aim, DateTime now)
    {
        var b = s.Body;
        var last = plan.Segments.LastOrDefault();
        var game = catalogue.Find("game");
        if (s.Contract.GamesOnly && game is not null) return Write(s, plan, game, SegmentRole.Advancing, "a session of games");

        // A written aim follows its pattern.
        if (aim.Written.Length > 0 && catalogue.FindAim(aim.Written) is { } shape)
        {
            var purpose = shape.Pattern[aim.Phase++ % shape.Pattern.Length];
            if (!Eligible(s, a, purpose)) purpose = aim.Purpose;
            var role = purpose == aim.Purpose ? SegmentRole.Advancing : purpose == Purpose.Cool ? SegmentRole.Supporting : SegmentRole.Side;
            var part = Improvise(s, a, plan, aim, purpose, role, $"{shape.Name}: {Purposes.Name(purpose).ToLowerInvariant()}");
            part.Seconds = r.Next(shape.Seconds[0], shape.Seconds[1] + 1);
            // Inside a shape like this the edges come fast: there is no settling into them.
            if (purpose == Purpose.Control && role == SegmentRole.Side) { part.Floor = Math.Max(part.Floor, 0.85); part.Type = Label(part, plan, s); }
            return part;
        }

        bool pacing = a.Pacing.DommeSets;
        bool rested = last is { Purpose: Purpose.Cool } or { Type: "tease-rest" };
        // What his state asks for comes first.
        if (pacing && !rested && aim.Purpose is Purpose.Control or Purpose.Ache && b.Control < 0.25)
            return Improvise(s, a, plan, aim, r.Chance(60) ? Purpose.Cool : Purpose.Starve, SegmentRole.Supporting, $"his control is thin ({b.Control:P0}): ease off so he can go on", handsOff: true);
        if (pacing && !rested && b.Overwhelm >= 0.9 && r.Chance(s.Contract.Punishment ? 30 : 60))
            return Improvise(s, a, plan, aim, Purpose.Cool, SegmentRole.Supporting, $"overwhelm at {b.Overwhelm:P0}: a short break so he can take more");
        if (pacing && !rested && b.CurrentAbove80 >= 300)
            return Improvise(s, a, plan, aim, r.Chance(50) ? Purpose.Cool : Purpose.Starve, SegmentRole.Supporting, $"{b.CurrentAbove80 / 60:0} minutes over 80%: change the level", handsOff: true);

        // Something written, when it is allowed, serves the aim, is fresh, and its turn comes up.
        int sinceWritten = plan.Segments.AsEnumerable().Reverse().TakeWhile(x => !x.Written).Count();
        if (aim.Segments >= 1 && sinceWritten >= 2 && last is { Role: SegmentRole.Advancing })
        {
            double lens = catalogue.Lens(s.Mood.Mood, "game");
            if (r.Chance(30 * (lens > 1 ? 1.3 : 1)))
            {
                var candidates = catalogue.Types
                    .Where(t => t.Written && t.Planned && Allowed(s, a, t.Needs, t.Persona) && (t.Fits.Length == 0 || t.Fits.Contains(aim.Purpose)))
                    .Select(t =>
                    {
                        double w = t.Weight * (t.Has("game") ? lens : 1);
                        int uses = plan.Segments.Count(x => x.Type == t.Id);
                        w /= 1 + uses * 1.5;
                        if (plan.Segments.TakeLast(4).Any(x => x.Type == t.Id)) w *= 0.05;
                        if (s.Recall.LastSegments.Contains(t.Id, StringComparer.OrdinalIgnoreCase)) w *= 0.6;
                        return (t, w);
                    }).Where(x => x.w > 0).ToList();
                if (candidates.Count > 0)
                {
                    var type = r.Weighted(candidates, candidates[0].t);
                    var role = type.Purpose == aim.Purpose && !type.Has("game") ? SegmentRole.Advancing : type.Has("game") || type.Purpose is Purpose.Cool or Purpose.Starve ? SegmentRole.Supporting : SegmentRole.Side;
                    return Write(s, plan, type, role, type.Has("game") ? $"a game, with stakes from the aim ({Purposes.Name(aim.Purpose).ToLowerInvariant()})" : $"its turn came up: {type.Goal}");
                }
            }
        }

        // A side of something else, because it suits her.
        if (last is { Role: SegmentRole.Advancing } && aim.Segments >= 1)
        {
            if (aim.Purpose is Purpose.Control or Purpose.Ache && Eligible(s, a, Purpose.Overwhelm) && s.Persona.Harshness >= 3
                && r.Chance(4 + s.Mean * 0.8 + s.Mood.Annoyance * 25 + (s.Contract.Punishment ? 10 : 0) + (s.Mood.Mood == DommeMood.Cruel ? 8 : 0)))
                return Improvise(s, a, plan, aim, Purpose.Overwhelm, SegmentRole.Side,
                    s.Mood.Annoyance >= 0.4 ? $"a side of torment: she is annoyed ({s.Mood.Cause})" : $"a side of torment in the middle of it: she is {s.Mood.Mood.ToString().ToLowerInvariant()}");
            if (aim.Purpose == Purpose.Overwhelm && Eligible(s, a, Purpose.Control) && r.Chance(12))
                return Improvise(s, a, plan, aim, Purpose.Control, SegmentRole.Side, "a few edges in the middle of it, while he is overwhelmed");
        }

        return Improvise(s, a, plan, aim, aim.Purpose, SegmentRole.Advancing, Advance(s, aim));
    }

    private static string Advance(SessionState s, Aim aim)
    {
        var b = s.Body;
        return aim.Purpose switch
        {
            Purpose.Ache => $"more of the ache: need at {b.Need:P0}, she wants {aim.Target:P0}",
            Purpose.Control => $"more edging: control at {b.Control:P0}, she wants it at {aim.Target:P0}",
            Purpose.Overwhelm => $"more of it: overwhelm at {b.Overwhelm:P0}, she wants {aim.Target:P0} and to keep it there",
            _ => "still next to nothing",
        };
    }

    private PlannedSegment Write(SessionState s, SessionPlan plan, SegmentType type, SegmentRole role, string reason, string origin = "plan")
    {
        double share = type.Share[0] + r.NextDouble() * (type.Share[1] - type.Share[0]);
        int min = type.Steps[0], max = type.Steps[1];
        var seg = new PlannedSegment
        {
            Type = type.Id, Purpose = type.Purpose, Role = role, Written = true, Reason = reason, Origin = origin, Aim = plan.AimIndex < plan.Aims.Count ? plan.AimIndex : -1,
            Share = Math.Round(Math.Max(0.01, share), 3), MinSteps = min, MaxSteps = Math.Max(min, max),
        };
        if (type.Has("countdown"))
        {
            // The number is the point: she says it up front and counts it down.
            seg.Announce = r.Next(min, max + 1);
            seg.MinSteps = seg.MaxSteps = seg.Announce;
        }
        return seg;
    }

    /// <summary>
    /// An improvised segment: she chooses its parameters from his state, the aim, her mood and the
    /// session's sliders. The steerer makes the steps; the name is only the shape that came out.
    /// </summary>
    public PlannedSegment Improvise(SessionState s, Authorities a, SessionPlan plan, Aim? aim, Purpose purpose, SegmentRole role, string reason,
        string origin = "plan", bool handsOff = false)
    {
        var b = s.Body;
        double intensity = s.Intensity / 10.0;
        // How far through the aim she is: she starts easier and tightens.
        double through = aim is { Started: true } && aim.Share > 0 && s.Budget > 0 ? Math.Clamp((s.Points - aim.PointsAtStart) / (aim.Share * s.Budget), 0, 1) : 0;
        bool harshMood = s.Mood.Mood is DommeMood.Cruel or DommeMood.Relentless || s.Retaliation.Active;
        double lengthFactor = Math.Clamp(Minutes(s) / 35, 0.6, 1.4);
        var seg = new PlannedSegment
        {
            Purpose = purpose, Role = role, Reason = reason, Origin = origin, Aim = aim is null ? -1 : plan.Aims.IndexOf(aim),
            Seconds = role == SegmentRole.Advancing ? (int)(r.Next(110, 221) * lengthFactor) : r.Next(45, 101),
        };
        int count = r.Next(1, 5);
        switch (purpose)
        {
            case Purpose.Control:
                seg.Edges = true;
                seg.Floor = !a.Pacing.DommeSets ? 1
                    : Math.Round(Math.Clamp(0.6 + 0.25 * through + (harshMood ? 0.08 : 0) + (intensity - 0.5) * 0.15 + (r.NextDouble() - 0.5) * 0.16, 0.5, 0.95), 2);
                if (seg.Floor >= 0.9) seg.Floor = 1;
                int kind = r.Next(0, 10);
                seg.HoldChance = kind < 3 ? 0 : kind < 8 ? Math.Round(0.3 + r.NextDouble() * 0.2, 2) : 1;
                seg.HoldFraction = Math.Round(Math.Min(0.6, 0.22 + s.Mean * 0.02 + (seg.HoldChance >= 1 ? 0.15 : 0)), 2);
                // A fast run of edges is one thing and a long hold another: not both at once.
                if (seg.Floor >= 1 && seg.HoldChance >= 1) seg.Floor = 0.75;
                seg.Announce = a.Pacing.DommeSets && b.Control >= 0.8 && r.Chance(20) ? r.Next(2, 4) : 0;
                seg.SpeedLow = Math.Clamp(4 + (int)Math.Round(intensity * 2 + through), 1, 10);
                seg.SpeedHigh = Math.Clamp(seg.SpeedLow + 2 + (seg.Floor >= 1 ? 1 : 0), 1, 10);
                Methods(s, a, seg, MethodFit.Choose(s, Purpose.Control, count, r, through >= 0.5 ? 2 : 1));
                break;
            case Purpose.Ache:
                seg.Edges = a.Edges.DommeSets && (!a.Pacing.DommeSets || r.Chance(55)) && s.Progress >= 0.05;
                // Now and then the stroking comes with an order: stay away from the edge. Reaching it costs him.
                seg.KeepUnder = !seg.Edges && a.Pacing.DommeSets && a.Edges.DommeSets && b.Need >= 0.25 && role == SegmentRole.Advancing
                    && !plan.Segments.TakeLast(5).Any(x => x.KeepUnder || x.Type == "no-edge") && r.Chance(22);
                if (seg.KeepUnder) seg.Seconds = Math.Min(seg.Seconds, r.Next(90, 151));
                seg.Floor = Math.Round(0.45 + r.NextDouble() * 0.2, 2);
                seg.SpeedLow = Math.Clamp(2 + (int)Math.Round(intensity * 2), 1, 10);
                seg.SpeedHigh = Math.Clamp(seg.SpeedLow + 2, 1, 10);
                Methods(s, a, seg, MethodFit.Choose(s, Purpose.Ache, Math.Min(count, 3), r));
                break;
            case Purpose.Overwhelm:
                seg.SpeedLow = Math.Clamp(5 + (int)Math.Round(intensity * 2) + (s.Retaliation.Active || s.Mood.Annoyance > 0.6 ? 1 : 0), 1, 10);
                seg.SpeedHigh = Math.Clamp(seg.SpeedLow + 2, 1, 10);
                Methods(s, a, seg, MethodFit.Choose(s, Purpose.Overwhelm, Math.Min(count, 3), r));
                break;
            case Purpose.Starve:
                // Nothing at all, or the lightest touch: she moves between them rather than stacking either.
                bool lastHandsOff = plan.Segments.LastOrDefault() is { Purpose: Purpose.Starve or Purpose.Cool, SpeedHigh: 0 };
                if (handsOff || role != SegmentRole.Advancing || !lastHandsOff && r.Chance(40))
                    seg.Seconds = r.Next(30, 71);
                else
                {
                    seg.SpeedLow = 1;
                    seg.SpeedHigh = 2;
                    Methods(s, a, seg, MethodFit.Choose(s, Purpose.Starve, Math.Min(count, 2), r));
                    seg.Seconds = (int)(r.Next(90, 181) * lengthFactor);
                }
                break;
            default:
            {
                // Long enough to bring him down, and to let what overwhelms him ease.
                double rest = Math.Max(Body.RestFor(s.Profile, b.Need, Math.Max(b.Closeness, 0.5), 0.45), b.Overwhelm >= 0.8 ? 40 : 0);
                if (b.Control < 0.4) rest = Math.Max(rest, (0.6 - b.Control) * 45 + 8);
                if (s.Contract.Punishment || s.Mean >= 8) rest *= 0.7;
                seg.Seconds = (int)Math.Clamp(rest, 15, 60);
                break;
            }
        }
        seg.Type = aim?.Label is { Length: > 0 } label && purpose == Purpose.Overwhelm ? label : Label(seg, plan, s);
        return seg;
    }

    private static void Methods(SessionState s, Authorities a, PlannedSegment seg, List<MethodInfo> methods)
    {
        if (!a.Methods.DommeChooses) return;
        seg.MethodIds = methods.Select(m => m.Id).ToList();
        seg.MethodNames = methods.Select(m => m.Name).ToList();
    }

    /// <summary>The name of the shape her parameters make.</summary>
    private static string Label(PlannedSegment seg, SessionPlan plan, SessionState s) => seg.Purpose switch
    {
        Purpose.Control => seg.HoldChance >= 1 ? "long-hold" : seg.Floor >= 0.85 ? "rapid-fire" : "edge-steady",
        Purpose.Ache => seg.Edges ? "edge-fresh" : seg.KeepUnder ? "keep-under" : s.Progress < 0.12 ? "slow-build" : "stroke-steady",
        Purpose.Overwhelm => seg.MethodIds.Count == 1 ? "hated-grip" : "torment",
        Purpose.Starve => seg.SpeedHigh > 0 ? "tempo-crawl" : "tease-rest",
        _ => "breather",
    };

    private static bool Allowed(SessionState s, Authorities a, string[] needs, Dictionary<string, int> dials)
    {
        foreach (var need in needs)
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
        var persona = s.Persona;
        foreach (var (dial, min) in dials)
        {
            int value = dial switch
            {
                "harsh" => persona.Harshness, "humiliation" => persona.Humiliation, "manipulation" => persona.Manipulation, _ => persona.Talkativeness,
            };
            if (value < min) return false;
        }
        return true;
    }

    // ── Events ──

    /// <summary>Adds an aim before the ending: the session runs a little longer.</summary>
    public void Extend(SessionState s, Authorities a, DateTime now, double share, string why)
    {
        var plan = s.Plan;
        if (plan is null) return;
        // The ending she had reached waits.
        plan.Segments.RemoveAll(x => x.Type == SegmentCatalogue.Ending);
        plan.Index = Math.Min(plan.Index, plan.Segments.Count);
        var (purpose, reason) = Choose(s, a, plan, s.Body, 0.95, plan.Aims.LastOrDefault()?.Purpose);
        plan.Aims.Add(NewAim(s, a, plan, purpose, share, $"{why}: {reason}", why, s.Body));
        Log(s, now, $"{why}: added {Purposes.Name(purpose)}");
    }

    /// <summary>
    /// He begged for a rest. In the middle of overwhelm she may give it: a break, then edges
    /// instead, then back to what she was doing (mercy with a catch).
    /// </summary>
    public RestBegOutcome OnRestBeg(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan?.Current is not { Purpose: Purpose.Overwhelm, Written: false } seg || plan.CurrentAim is not { } aim) return RestBegOutcome.NoRule;
        double grant = seg.Type == "torment" ? 0.3 : 0.2;
        if (!r.Chance(grant * 100 * (1 - s.Mood.Annoyance * 0.5))) return RestBegOutcome.Denied;
        seg.Stop = true;
        seg.Ended = "he begged, and she let him stop";
        var parts = new List<string>();
        int at = plan.Index + 1;
        if (Eligible(s, a, Purpose.Control))
        {
            var catchSeg = Improvise(s, a, plan, aim, Purpose.Control, SegmentRole.Side, "mercy with a catch: he gets his break, then edges instead", "rest beg");
            catchSeg.Seconds = r.Next(40, 81);
            plan.Segments.Insert(at, catchSeg);
            parts.Add("then edges");
        }
        if (AimDone(s, aim, now) is null) parts.Add($"then back to {Purposes.Name(aim.Purpose).ToLowerInvariant()}");
        Log(s, now, $"granted a rest beg during {seg.Type}: a rest" + (parts.Count > 0 ? ", " + string.Join(", ", parts) : ""));
        return RestBegOutcome.Granted;
    }

    /// <summary>He keeps asking to cum: the ending moves further away.</summary>
    public void OnPestering(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        // Twice at most: after that asking again changes nothing, and the night still ends.
        if (plan is null || s.RequestsDenied < 3 || plan.Pestered >= 2 || !r.Chance(50) || plan.AtEnding) return;
        plan.Pestered++;
        var purpose = Eligible(s, a, Purpose.Control) && r.Chance(60) ? Purpose.Control : Eligible(s, a, Purpose.Overwhelm) ? Purpose.Overwhelm : Purpose.Ache;
        if (!Eligible(s, a, purpose)) return;
        plan.Aims.Add(NewAim(s, a, plan, purpose, 0.08, "he kept asking to cum", "pestering", s.Body));
        Log(s, now, $"he kept asking: added {Purposes.Name(purpose)} before the ending");
    }

    /// <summary>He failed a challenge: a punishment comes next, then back to what she was doing.</summary>
    public void OnChallengeFailed(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan is null) return;
        // The challenge is over the moment he fails it.
        if (plan.Current is { } seg) { seg.Stop = true; seg.Ended = "he failed it"; }
        if (!Eligible(s, a, Purpose.Overwhelm)) return;
        var punish = Improvise(s, a, plan, plan.CurrentAim, Purpose.Overwhelm, SegmentRole.Side, "he failed a challenge: he pays for it", "failed a challenge");
        punish.Type = "punish";
        punish.Seconds = r.Next(45, 91);
        plan.Segments.Insert(Math.Min(plan.Index + 1, plan.Segments.Count), punish);
        Log(s, now, "failed a challenge: a punishment next");
    }

    /// <summary>An orgasm without permission: whatever she was pursuing is over; the rest is punishment.</summary>
    public void OnUnauthorizedOrgasm(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan is null) return;
        if (plan.Current is { Started: true } seg) { seg.Stop = true; seg.Ended = "he came without permission"; }
        plan.Segments.RemoveRange(Math.Min(plan.Index + 1, plan.Segments.Count), Math.Max(0, plan.Segments.Count - plan.Index - 1));
        if (plan.CurrentAim is { Started: true } aim) CloseAim(s, plan, aim, now, "he came without permission");
        plan.Aims.RemoveRange(plan.AimIndex, plan.Aims.Count - plan.AimIndex);
        var purpose = Eligible(s, a, Purpose.Overwhelm) ? Purpose.Overwhelm : Eligible(s, a, Purpose.Control) ? Purpose.Control : Purpose.Ache;
        var punishment = NewAim(s, a, plan, purpose, 0.15, "he came without permission: the rest is punishment", "came without permission", s.Body);
        punishment.Label = "punish";
        punishment.Written = "";
        plan.Aims.Add(punishment);
        Log(s, now, $"came without permission: dropped her aims; {Purposes.Name(purpose)} as punishment");
    }

    /// <summary>The contract changed mid-session: the aims not yet begun are chosen again.</summary>
    public void OnContractChanged(SessionState s, Authorities a, DateTime now)
    {
        var plan = s.Plan;
        if (plan is null) return;
        plan.Segments.RemoveRange(Math.Min(plan.Index + 1, plan.Segments.Count), Math.Max(0, plan.Segments.Count - plan.Index - 1));
        if (plan.Current is { Started: true } seg && seg.Type != SegmentCatalogue.Ending) { seg.Stop = true; seg.Ended = "the settings changed"; }
        if (plan.CurrentAim is { Started: true } aim) CloseAim(s, plan, aim, now, "the settings changed");
        plan.Aims.RemoveRange(plan.AimIndex, plan.Aims.Count - plan.AimIndex);
        double left = Math.Max(0.1, 1 - s.Progress);
        var (purpose, reason) = Choose(s, a, plan, s.Body, s.Progress, plan.Aims.LastOrDefault()?.Purpose);
        plan.Aims.Add(NewAim(s, a, plan, purpose, left, reason, "settings changed", s.Body));
        Log(s, now, "settings changed: chose her aims again");
    }

    /// <summary>
    /// Roughly what one step of a written segment earns in points on his methods, by the same rules
    /// that score it. Rests earn nothing; a game is guessed.
    /// </summary>
    public static double StepPoints(SessionState s, SegmentType t)
    {
        var avg = MethodInfo.UserChoice(s.Methods);
        var st = t.Step;
        static double Mid(int[] a) => a.Length == 0 ? 0 : (a[0] + a[^1]) / 2.0;
        int speed = (int)Math.Round(Mid(st.Speed));
        double Time(double seconds) => Points.ForStimulation(avg, seconds, speed);
        double toEdge = Math.Min(150, Body.ExpectedSeconds(s.Profile, avg, speed, s.Body.Need, 0.6));
        double edge = avg.EdgePoints + Time(toEdge);
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

    private static void Log(SessionState s, DateTime now, string what)
    {
        var plan = s.Plan!;
        plan.Revisions.Add($"{(now - s.StartedAtUtc).TotalMinutes:0.0} min, step {s.StepNumber}: {what}");
        if (plan.Revisions.Count > 80) plan.Revisions.RemoveAt(0);
    }

    private static string Slug(string origin) => origin.Replace(' ', '-');
}

/// <summary>
/// Which methods fit a purpose. Methods are chosen for what they make him experience (how close
/// they bring him, how intense, how torturous, how teasing); variety comes last. A segment may use
/// one method or several, as long as each fits.
/// </summary>
public static class MethodFit
{
    /// <summary>How well a method serves a purpose, 0 to 1.</summary>
    public static double Score(MethodInfo m, Purpose p)
    {
        double close = m.Closeness / 10.0, intense = m.Intensity / 10.0, torture = m.TortLevel / 10.0, tease = m.Tease / 10.0;
        return p switch
        {
            // Pleasure that brings him close and keeps him there: not torture, not a tease.
            // Intense methods build need too, but they belong to testing his control: here the milder the better.
            Purpose.Ache => close * (1 - torture * 0.8) * (1 - tease * 0.3) * (1.15 - intense * 0.85),
            // Gets him to the edge, and is hard to hold back from.
            Purpose.Control => close * (0.4 + 0.6 * intense) * (1 - torture * 0.6),
            Purpose.Overwhelm => torture * (0.6 + 0.4 * intense),
            Purpose.Starve => tease * (1 - close * 0.7) * (1 - intense * 0.6) * (1 - torture * 0.5),
            _ => 0,
        };
    }

    /// <summary>The intensity ceiling for a purpose, from the session's intensity slider.</summary>
    public static int Ceiling(SessionState s, Purpose p, int raise = 0) => Math.Clamp(s.Intensity + raise + p switch
    {
        Purpose.Control => 1, Purpose.Overwhelm => 3, Purpose.Starve => -1, _ => -1,
    }, 2, 10);

    /// <summary>Up to <paramref name="count"/> methods that all fit the purpose.</summary>
    public static List<MethodInfo> Choose(SessionState s, Purpose p, int count, IRandom r, int raise = 0)
    {
        var all = s.Methods;
        if (all.Count == 0) return [];
        int ceiling = Ceiling(s, p, raise);
        var pool = all.Where(m => m.Intensity <= ceiling).ToList();
        if (pool.Count == 0) pool = all.ToList();
        // What the purpose cannot do without.
        var must = p switch
        {
            Purpose.Control => pool.Where(m => m.Closeness >= MethodPreference.EdgeReach).ToList(),
            Purpose.Ache => pool.Where(m => m.Closeness >= MethodPreference.EdgeReach && m.TortLevel < 5 && m.Intensity <= 6).ToList() is { Count: > 0 } mild ? mild
                : pool.Where(m => m.Closeness >= MethodPreference.EdgeReach && m.TortLevel < 5).ToList(),
            Purpose.Overwhelm => pool.Where(m => m.TortLevel >= 5).ToList() is { Count: > 0 } t ? t
                : all.Where(m => m.TortLevel >= 5 && m.Intensity <= ceiling + 2).ToList(),
            Purpose.Starve => pool.Where(m => m.Tease >= 4 && m.TortLevel < 5).ToList(),
            _ => pool,
        };
        if (must.Count == 0 && p is Purpose.Control or Purpose.Ache) must = all.Where(m => m.Closeness >= MethodPreference.EdgeReach).ToList();
        if (must.Count == 0) must = pool.OrderByDescending(m => Score(m, p)).Take(3).ToList();
        pool = must;
        // Once lube is on, dry methods are impractical.
        if (s.LubeActive && pool.Where(m => m.RequiresLube).ToList() is { Count: > 0 } lubed) pool = lubed;

        double best = pool.Max(m => Score(m, p));
        var fitting = pool.Where(m => Score(m, p) >= best * 0.6).ToList();
        var chosen = new List<MethodInfo>();
        while (chosen.Count < count && fitting.Count > 0)
        {
            var weighted = fitting.Select(m =>
            {
                double w = Math.Pow(Math.Max(0.01, Score(m, p)), 2);
                int index = s.RecentMethodIds.LastIndexOf(m.Id);
                if (index >= 0) w *= 0.5 + 0.5 * (s.RecentMethodIds.Count - index) / (double)(s.RecentMethodIds.Count + 1);
                if (!s.LubeActive && m.RequiresLube) w *= 0.5;
                if (s.Recall.FavoriteMethods.Contains(m.Id) && p is Purpose.Ache or Purpose.Control) w *= 1.3;
                return (m, w);
            }).ToList();
            var pick = r.Weighted(weighted, fitting[0]);
            chosen.Add(pick);
            fitting.Remove(pick);
            // Lube or no lube: one segment does not mix them.
            fitting = fitting.Where(m => m.RequiresLube == chosen[0].RequiresLube || s.LubeActive).ToList();
        }
        return chosen;
    }
}
