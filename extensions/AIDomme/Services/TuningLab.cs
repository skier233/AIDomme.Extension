using AIDomme.Data;

namespace AIDomme.Services;

/// <summary>One thing the user can be asked to do: a method for a time, a set of edges on it, or a punishment ending.</summary>
public sealed record LabCandidate(string Key, string Group, string Title, string Description, string[] Meta, double Points, int Wins, int Losses);

/// <summary>A side of a matchup, with the model's belief that it is the harder of the two.</summary>
public sealed record LabSide(LabCandidate Candidate, double HarderChance);

/// <summary>What a method is worth now, and (when asked for) what Apply would set from the comparisons so far.</summary>
public sealed record LabMethodRow(int Id, string Name, double CurrentPerMinute, double? SuggestedPerMinute, double? CurrentPerEdge, double? SuggestedPerEdge, int Comparisons);

public sealed record TuningLabView(
    string Mode,
    int Comparisons,
    int YourComparisons,
    double Confidence,
    LabSide? Left,
    LabSide? Right,
    IReadOnlyList<LabMethodRow> Methods,
    IReadOnlyList<LabCandidate> Enders,
    int OptimizeNeeds,
    double PointsPerMinute)
{
    /// <summary>For deals: each deal's value now and what the answers suggest.</summary>
    public IReadOnlyList<LabDealRow> Deals { get; init; } = [];
}

/// <summary>An answer. <paramref name="Recent"/> lists the pairs the page just showed ("a|b,c|d", newest last), so the next is something else.</summary>
public sealed record ComparisonRequest(string Mode, string WinnerKey, string LoserKey, int Strength, string? Recent = null);

/// <summary>
/// What he has just seen or answered, so the next matchup is something else: neither the same pair
/// nor the same two subjects (methods, endings or deals, whatever the amounts) come back while they
/// are recent, and a subject shown lately is avoided a little.
/// </summary>
public sealed class LabRecent
{
    private readonly HashSet<string> pairs = [];
    private readonly HashSet<string> subjectPairs = [];
    private readonly Dictionary<string, int> subjects = [];

    public LabRecent(IEnumerable<(string A, string B)> recent)
    {
        foreach (var (a, b) in recent)
        {
            pairs.Add(Join(a, b));
            subjectPairs.Add(Join(Subject(a), Subject(b)));
            foreach (var s in new[] { Subject(a), Subject(b) }) subjects[s] = subjects.GetValueOrDefault(s) + 1;
        }
    }

    public static LabRecent None { get; } = new([]);

    /// <summary>Pairs from the page ("a|b,c|d") and his last answers in the mode, newest last.</summary>
    public static LabRecent From(string? shown, IEnumerable<TuningComparisonEntry> answered)
    {
        var fromPage = (shown ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('|')).Where(x => x.Length == 2).Select(x => (x[0], x[1])).TakeLast(8);
        return new(answered.Select(x => (x.WinnerKey, x.LoserKey)).Concat(fromPage));
    }

    /// <summary>What a method, ending or deal is, whatever the amount.</summary>
    public static string Subject(string key)
    {
        var parts = key.Split(':');
        return parts[0] is "stim" or "offer" && parts.Length > 1 ? $"{parts[0]}:{parts[1]}" : key;
    }

    private static string Join(string a, string b) => string.CompareOrdinal(a, b) < 0 ? $"{a}|{b}" : $"{b}|{a}";

    /// <summary>A multiplier on how worth asking a pair is: 0 for a repeat, small for the same subjects again.</summary>
    public double Penalty(string a, string b)
    {
        string sa = Subject(a), sb = Subject(b);
        if (pairs.Contains(Join(a, b)) || subjectPairs.Contains(Join(sa, sb))) return 0;
        return Math.Pow(0.5, Math.Min(3, subjects.GetValueOrDefault(sa))) * Math.Pow(0.5, Math.Min(3, subjects.GetValueOrDefault(sb)));
    }

    /// <summary>One of the best few, weighted by how worth asking each is, rather than always the very best.</summary>
    public static (LabCandidate, LabCandidate)? PickAmong(List<((LabCandidate, LabCandidate) Pair, double Impact)> scored, Random random, int top = 6)
    {
        var best = scored.Where(x => x.Impact > 0 && double.IsFinite(x.Impact))
            .GroupBy(x => Join(x.Pair.Item1.Key, x.Pair.Item2.Key)).Select(g => g.First())
            .OrderByDescending(x => x.Impact).Take(top).ToList();
        if (best.Count == 0) return null;
        double total = best.Sum(x => x.Impact), roll = random.NextDouble() * total;
        foreach (var x in best)
            if ((roll -= x.Impact) <= 0) return x.Pair;
        return best[^1].Pair;
    }
}

/// <summary>
/// The calibration lab, ported from the original app: the user is shown two assignments side by
/// side and says which they would rather be given (the easier one), and how strongly. A
/// Bradley-Terry model over per-method rates learns from every answer; the next matchup is the one
/// it is least sure about, favouring candidates it has seen least, the same amount on two methods,
/// and a method's time against its edges. <see cref="StimMultiplierOptimizer"/> turns the same
/// history into the points a method earns in a session.
/// </summary>
public static class TuningLab
{
    public const string Stim = "Stim", Ender = "Ender";
    private const double PointsScale = 800;
    private static readonly int[] TimeSteps = [15, 30, 45, 60, 75, 90, 105, 120];

    public static bool IsMode(string mode) => mode is Stim or Ender or DealLab.Mode;

    /// <summary>Every option that may appear in a mode's matchups (deals are also set against timed method assignments).</summary>
    public static HashSet<string> Keys(string mode, IReadOnlyList<StimMethod> methods, IReadOnlyList<PunishmentEnder> enders, IReadOnlyList<OfferDefinition> offers)
        => mode == DealLab.Mode
            ? DealLab.Candidates(offers).Concat(Candidates(Stim, methods, []).Where(x => x.Group == "Timed")).Select(x => x.Key).ToHashSet()
            : Candidates(mode, methods, enders).Select(x => x.Key).ToHashSet();

    public static List<LabCandidate> Candidates(string mode, IReadOnlyList<StimMethod> methods, IReadOnlyList<PunishmentEnder> enders)
    {
        var list = new List<LabCandidate>();
        if (mode == Ender)
        {
            foreach (var e in enders)
                list.Add(new($"ender:{e.Name}", "Ending", e.Name, string.Join(" ", new[] { e.AnnouncementText, e.InstructionText }.Where(x => !string.IsNullOrWhiteSpace(x))),
                    [$"Severity {e.Severity}/10", $"Humiliation {e.Humiliation}/10", e.EndsInOrgasm ? "Ends in an orgasm" : "No orgasm"], 1000, 0, 0));
            return list;
        }
        foreach (var m in methods)
        {
            var (time, edge) = StimMethodPointMath.GetSeedMultipliers(m);
            string[] meta = [$"Intensity {m.Intensity}/10", $"Tease {m.Tease}/10", $"Torture {m.TortLevel}/10", $"Closeness {m.Closeness}/10", m.RequiresLube ? "Lube" : "No lube"];
            foreach (var s in TimeSteps)
                list.Add(new($"stim:{m.Id}:time:{s}", "Timed", $"{m.Name} for {Seconds(s)}", m.Description, meta, time * s, 0, 0));
            if (m.Closeness >= 3)
                for (int n = 1; n <= 5; n++)
                    list.Add(new($"stim:{m.Id}:edge:{n}", "Edges", $"{(n == 1 ? "1 edge" : $"{n} edges")} using {m.Name}", m.Description, meta, edge * n, 0, 0));
        }
        return list;
    }

    private static string Seconds(int s) => s % 60 == 0 ? s == 60 ? "a minute" : $"{s / 60} minutes" : s > 60 ? $"{s / 60} min {s % 60}s" : $"{s} seconds";

    /// <summary>Builds the lab's view: the fitted model, the next matchup and the method table.</summary>
    public static TuningLabView View(string mode, IReadOnlyList<StimMethod> methods, IReadOnlyList<PunishmentEnder> enders,
        IReadOnlyList<TuningComparisonEntry> history, int? userId, double pointsPerMinute, Random random, LabRecent? recent = null)
    {
        recent ??= LabRecent.None;
        var modeHistory = history.Where(x => x.Mode == mode).ToList();
        var candidates = Candidates(mode, methods, enders);
        var known = candidates.Select(x => x.Key).ToHashSet();
        var wins = modeHistory.GroupBy(x => x.WinnerKey).ToDictionary(g => g.Key, g => g.Count());
        var losses = modeHistory.GroupBy(x => x.LoserKey).ToDictionary(g => g.Key, g => g.Count());

        var model = new Model();
        foreach (var m in methods)
        {
            var (time, edge) = StimMethodPointMath.GetSeedMultipliers(m);
            model.SetSeed(m.Id, time * 120 / PointsScale, edge * 5 / PointsScale);
        }
        foreach (var e in enders) model.SetPrior($"ender:{e.Name}", (e.Severity - 5) * 0.3);
        model.Fit(modeHistory.Where(x => known.Contains(x.WinnerKey) && known.Contains(x.LoserKey))
            .Select(x => (Harder: x.LoserKey, Easier: x.WinnerKey, Weight: x.Strength switch { 1 => 0.5, 3 => 2.0, _ => 1.0 })).ToList());

        candidates = candidates.Select(c => c with
        {
            Points = mode == Ender ? 1000 + model.Score(c.Key) * 173.72 : model.Score(c.Key) * PointsScale,
            Wins = wins.GetValueOrDefault(c.Key),
            Losses = losses.GetValueOrDefault(c.Key),
        }).ToList();

        LabSide? left = null, right = null;
        if (NextPair(mode, candidates, model, random, recent) is { } pair)
        {
            double p = model.ProbHarder(pair.Item1.Key, pair.Item2.Key);
            (left, right) = (new LabSide(pair.Item1, p), new LabSide(pair.Item2, 1 - p));
        }

        var rows = mode == Stim ? MethodRows(methods, modeHistory, null) : [];

        var keys = candidates.Select(x => x.Key).ToList();
        int stimHistory = history.Count(x => x.Mode == Stim);
        return new(mode, modeHistory.Count, modeHistory.Count(x => x.UserId == userId), model.Confidence(keys, random),
            left, right, rows, mode == Ender ? candidates.OrderByDescending(x => x.Points).ToList() : [],
            Math.Max(0, 25 - stimHistory), pointsPerMinute);
    }

    /// <summary>
    /// What Apply would set for each method: the optimizer's fit over every stim comparison, anchored
    /// to the point scale the methods use now. It takes a second or two, so the lab asks for it
    /// separately from the next matchup. (The live model only chooses matchups: its rates are
    /// relative, not points.)
    /// </summary>
    public static IReadOnlyList<LabMethodRow> Suggestions(IReadOnlyList<StimMethod> methods, IReadOnlyList<TuningComparisonEntry> history)
    {
        IReadOnlyDictionary<int, (double TimePointsPerSecond, double EdgePointsPerEdge)>? fit = null;
        try { fit = StimMultiplierOptimizer.Optimize(methods, history).Multipliers; }
        catch (InvalidOperationException) { /* too few answers yet */ }
        return MethodRows(methods, history.Where(x => x.Mode == Stim).ToList(), fit);
    }

    private static List<LabMethodRow> MethodRows(IReadOnlyList<StimMethod> methods, List<TuningComparisonEntry> stimHistory,
        IReadOnlyDictionary<int, (double TimePointsPerSecond, double EdgePointsPerEdge)>? fit)
        => methods.Select(m =>
        {
            var (time, edge) = StimMethodPointMath.GetSeedMultipliers(m);
            var f = fit?.GetValueOrDefault(m.Id);
            bool edges = m.Closeness >= 3;
            int seen = stimHistory.Count(x => x.WinnerKey.StartsWith($"stim:{m.Id}:") || x.LoserKey.StartsWith($"stim:{m.Id}:"));
            return new LabMethodRow(m.Id, m.Name, time * 60, f is { } s ? s.TimePointsPerSecond * 60 : null,
                edges ? edge : null, edges && f is { } e ? e.EdgePointsPerEdge : null, seen);
        }).ToList();

    // ── Choosing the next matchup ──

    private static (LabCandidate, LabCandidate)? NextPair(string mode, List<LabCandidate> candidates, Model model, Random random, LabRecent recent)
    {
        if (candidates.Count < 2) return null;
        (LabCandidate, LabCandidate)? pair = null;
        if (mode == Stim)
        {
            double roll = random.NextDouble();
            if (roll < 0.30) pair = Targeted(candidates, model, recent, random, (a, b) => Model.SameMethodCrossType(a.Key, b.Key));
            else if (roll < 0.55) pair = Targeted(candidates, model, recent, random, (a, b) => Model.SameScaleCrossMethod(a.Key, b.Key));
        }
        pair ??= ByImpact(mode, candidates, model, recent, random) ?? Exploration(mode, candidates, model, recent);
        if (pair is null)
        {
            var a = candidates[random.Next(candidates.Count)];
            var rest = candidates.Where(c => c.Key != a.Key).ToList();
            pair = (a, rest[random.Next(rest.Count)]);
        }
        // Which side is shown on the left is arbitrary.
        return random.Next(2) == 0 ? pair : (pair.Value.Item2, pair.Value.Item1);
    }

    private static int Seen(LabCandidate c) => c.Wins + c.Losses;

    private static double Impact(string mode, LabCandidate a, LabCandidate b, Model model, LabRecent recent)
    {
        if (Model.Redundant(a.Key, b.Key)) return double.NegativeInfinity;
        double fresh = recent.Penalty(a.Key, b.Key);
        if (fresh <= 0) return double.NegativeInfinity;
        double uncertainty = model.Uncertainty(a.Key, b.Key);
        double evidence = 1 / Math.Sqrt(Seen(a) + 1.0) + 1 / Math.Sqrt(Seen(b) + 1.0);
        double sameScale = mode == Stim && Model.SameScaleCrossMethod(a.Key, b.Key) ? 0.75 : 0;
        double crossType = mode == Stim && Model.SameMethodCrossType(a.Key, b.Key) ? 0.45 : 0;
        double close = 1 / (1 + Math.Abs(a.Points - b.Points));
        return fresh * uncertainty * (1 + 0.65 * evidence + sameScale + crossType + 0.25 * close);
    }

    private static (LabCandidate, LabCandidate)? ByImpact(string mode, List<LabCandidate> candidates, Model model, LabRecent recent, Random random)
    {
        var scored = new List<((LabCandidate, LabCandidate), double)>();
        int samples = Math.Min(2500, candidates.Count * 16);
        for (int i = 0; i < samples; i++)
        {
            int x = random.Next(candidates.Count), y = random.Next(candidates.Count);
            if (x == y) continue;
            scored.Add(((candidates[x], candidates[y]), Impact(mode, candidates[x], candidates[y], model, recent)));
        }
        return LabRecent.PickAmong(scored, random);
    }

    private static (LabCandidate, LabCandidate)? Exploration(string mode, List<LabCandidate> candidates, Model model, LabRecent recent)
    {
        foreach (var anchor in candidates.OrderBy(Seen).Take(30))
            if (candidates.Where(c => c.Key != anchor.Key && !Model.Redundant(anchor.Key, c.Key) && recent.Penalty(anchor.Key, c.Key) > 0)
                    .OrderByDescending(c => Impact(mode, anchor, c, model, recent)).FirstOrDefault() is { } match)
                return (anchor, match);
        return null;
    }

    private static (LabCandidate, LabCandidate)? Targeted(List<LabCandidate> candidates, Model model, LabRecent recent, Random random,
        Func<LabCandidate, LabCandidate, bool> fits)
    {
        var scored = new List<((LabCandidate, LabCandidate), double)>();
        foreach (var a in candidates.OrderBy(Seen).Take(80))
            foreach (var b in candidates)
                if (a.Key != b.Key && fits(a, b)) scored.Add(((a, b), Impact(Stim, a, b, model, recent)));
        return LabRecent.PickAmong(scored, random);
    }

    /// <summary>The original app's intensity model: per-method rates from metadata, corrected by the comparisons.</summary>
    private sealed class Model
    {
        private const double MinTimeRate = 0.01, MinEdgeRate = 0.00625;
        private readonly Dictionary<int, double> seedTime = [], seedEdge = [], time = [], edge = [];
        private readonly Dictionary<string, double> enderScore = [], enderPrior = [];

        public static (string Type, int Method, int Raw, double Norm)? Parse(string key)
        {
            if (!key.StartsWith("stim:")) return null;
            var parts = key.Split(':');
            if (parts.Length != 4 || !int.TryParse(parts[1], out int id) || !int.TryParse(parts[3], out int raw)) return null;
            return (parts[2], id, raw, parts[2] == "time" ? raw / 120.0 : raw / 5.0);
        }

        public void SetSeed(int id, double timeRate, double edgeRate) => (seedTime[id], seedEdge[id]) = (timeRate, edgeRate);
        public void SetPrior(string key, double prior) => enderPrior[key] = prior;

        public (double Time, double Edge) Rates(int id)
            => (Math.Max(MinTimeRate, seedTime.GetValueOrDefault(id) + time.GetValueOrDefault(id)),
                Math.Max(MinEdgeRate, seedEdge.GetValueOrDefault(id) + edge.GetValueOrDefault(id)));

        public double Score(string key)
        {
            if (Parse(key) is { } k)
            {
                var (t, e) = Rates(k.Method);
                return (k.Type == "time" ? t : e) * k.Norm;
            }
            return key.StartsWith("ender:") ? enderPrior.GetValueOrDefault(key) + enderScore.GetValueOrDefault(key) : 0;
        }

        public double ProbHarder(string a, string b) => 1 / (1 + Math.Exp(-(Score(a) - Score(b))));
        public double Uncertainty(string a, string b) => 1 - Math.Abs(2 * ProbHarder(a, b) - 1);

        private void Nudge(string key, double g)
        {
            if (Parse(key) is { } k)
            {
                var rates = k.Type == "time" ? time : edge;
                rates[k.Method] = rates.GetValueOrDefault(k.Method) + g * k.Norm;
            }
            else if (key.StartsWith("ender:")) enderScore[key] = enderScore.GetValueOrDefault(key) + g;
        }

        public void Fit(List<(string Harder, string Easier, double Weight)> history, int epochs = 120)
        {
            if (history.Count == 0) return;
            double lr = 0.18;
            var rng = new Random(42);
            for (int epoch = 0; epoch < epochs; epoch++)
            {
                foreach (var (harder, easier, weight) in history.OrderBy(_ => rng.Next()))
                {
                    double g = lr * (1 - ProbHarder(harder, easier)) * weight;
                    Nudge(harder, g);
                    Nudge(easier, -g);
                }
                foreach (var k in time.Keys.ToList())
                {
                    time[k] *= 0.99;
                    if (seedTime.GetValueOrDefault(k) + time[k] < MinTimeRate) time[k] = MinTimeRate - seedTime.GetValueOrDefault(k);
                }
                foreach (var k in edge.Keys.ToList())
                {
                    edge[k] *= 0.99;
                    if (seedEdge.GetValueOrDefault(k) + edge[k] < MinEdgeRate) edge[k] = MinEdgeRate - seedEdge.GetValueOrDefault(k);
                }
                foreach (var k in enderScore.Keys.ToList()) enderScore[k] *= 0.99;
                lr *= 0.99;
            }
        }

        public double Confidence(List<string> keys, Random random)
        {
            if (keys.Count < 2) return 1;
            double total = 0;
            int n = 0;
            for (int i = 0; i < Math.Min(500, keys.Count * (keys.Count - 1) / 2); i++)
            {
                int a = random.Next(keys.Count), b = random.Next(keys.Count);
                if (a == b || Redundant(keys[a], keys[b])) continue;
                total += Uncertainty(keys[a], keys[b]);
                n++;
            }
            return n > 0 ? 1 - total / n : 1;
        }

        /// <summary>The same method, same kind, different amounts: the answer is obvious.</summary>
        public static bool Redundant(string a, string b)
            => Parse(a) is { } x && Parse(b) is { } y && x.Method == y.Method && x.Type == y.Type && x.Raw != y.Raw;

        public static bool SameScaleCrossMethod(string a, string b)
            => Parse(a) is { } x && Parse(b) is { } y && x.Method != y.Method && x.Type == y.Type && x.Raw == y.Raw;

        public static bool SameMethodCrossType(string a, string b)
            => Parse(a) is { } x && Parse(b) is { } y && x.Method == y.Method && x.Type != y.Type;
    }
}
