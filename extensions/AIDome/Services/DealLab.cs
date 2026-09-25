using AIDome.Data;
using AIDome.Sessions;

namespace AIDome.Services;

/// <summary>A deal's value range now, and what his answers suggest it should be.</summary>
public sealed record LabDealRow(int Id, string Name, string Kind, double CurrentLow, double CurrentHigh, double SuggestedLow, double SuggestedHigh,
    double Factor, int Comparisons);

/// <summary>
/// Side-by-side calibration of her deals. Each deal is shown at a few concrete amounts ("Edge 5 more
/// times, then you may cum", "3 days of denial"), and he says which he would rather accept. A quarter
/// of the matchups set a deal against a method assignment ("Palm for 2 minutes"), whose points are
/// already calibrated: that anchors deals to the same scale, so a deal's value can be corrected in
/// absolute terms, not only against other deals.
/// <para>
/// The model gives each deal one factor on its whole value (base, per unit, per hold second). A
/// candidate's value is its current points times that factor; the chance one side is the harder is
/// logistic in the log ratio of the two values, so a price twice the other is clearly harder whatever
/// the scale. Factors are fitted by gradient descent with a pull toward 1.
/// </para>
/// </summary>
public static class DealLab
{
    public const string Mode = "Offer";
    private const double Sharpness = 2.0;

    public static bool IsKey(string key) => key.StartsWith("offer:");

    public static List<LabCandidate> Candidates(IReadOnlyList<OfferDefinition> offers)
    {
        var list = new List<LabCandidate>();
        foreach (var d in offers.Where(x => x.Enabled))
        {
            var t = OfferTemplate.From(d);
            var buys = t.ForOrgasm ? OfferBuys.Orgasm : t.ForRest ? OfferBuys.Rest : OfferBuys.Mercy;
            int hold = d.MaxHold > 0 ? (d.MinHold + d.MaxHold) / 2 : 0;
            foreach (var amount in new[] { d.MinAmount, (d.MinAmount + d.MaxAmount) / 2, d.MaxAmount }.Distinct())
            {
                string what = buys switch { OfferBuys.Orgasm => "to cum", OfferBuys.Rest => "for a rest", _ => "to skip a punishment" };
                list.Add(new($"offer:{d.Id}:{amount}:{hold}", "Deal", $"{d.Name}: {Amount(d.Kind, amount)} {what}", Offers.Words(t, amount, hold, buys),
                    [Kind(d.Kind), d.Flavor.ToString()], Value(d, amount, hold), 0, 0));
            }
        }
        return list;
    }

    private static string Amount(OfferKind kind, int n) => kind switch
    {
        OfferKind.EdgesNow => n == 1 ? "1 edge now" : $"{n} edges now",
        OfferKind.EdgesLater => n == 1 ? "1 edge owed" : $"{n} edges owed",
        OfferKind.Denial => n == 1 ? "1 day of denial" : $"{n} days of denial",
        OfferKind.PunishmentSession => n == 1 ? "1 punishment session" : $"{n} punishment sessions",
        OfferKind.PunishmentEnding => "a punishment ending",
        OfferKind.Ruin => n == 1 ? "1 ruined orgasm" : $"{n} ruined orgasms",
        _ => "ruin it",
    };

    private static string Kind(OfferKind kind) => kind switch
    {
        OfferKind.EdgesNow => "Edges now", OfferKind.EdgesLater => "Edges owed", OfferKind.Denial => "Denial days",
        OfferKind.PunishmentSession => "Punishment session", OfferKind.PunishmentEnding => "Punishment ending",
        OfferKind.Ruin => "Ruined later", _ => "Ruined now",
    };

    public static double Value(OfferDefinition d, int amount, int hold) => d.BasePoints + amount * (d.PointsPerUnit + hold * d.PointsPerHoldSecond);

    public static (int Id, int Amount, int Hold)? Parse(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 4 && parts[0] == "offer" && int.TryParse(parts[1], out var id) && int.TryParse(parts[2], out var amount)
            && int.TryParse(parts[3], out var hold) ? (id, amount, hold) : null;
    }

    /// <summary>Fits each deal's factor from the answers. Keys other than deals keep their current points.</summary>
    public static Dictionary<int, double> Fit(IReadOnlyDictionary<string, double> points, IReadOnlyList<TuningComparisonEntry> history)
    {
        var factors = new Dictionary<int, double>();
        var answers = history.Where(x => x.Mode == Mode && points.ContainsKey(x.WinnerKey) && points.ContainsKey(x.LoserKey))
            .Select(x => (Harder: x.LoserKey, Easier: x.WinnerKey, Weight: x.Strength switch { 1 => 0.5, 3 => 2.0, _ => 1.0 })).ToList();
        if (answers.Count == 0) return factors;
        var log = new Dictionary<int, double>();
        double Score(string key) => Math.Log(Math.Max(0.5, points[key])) + (Parse(key) is { } k ? log.GetValueOrDefault(k.Id) : 0);
        double lr = 0.1;
        var rng = new Random(7);
        for (int epoch = 0; epoch < 200; epoch++)
        {
            foreach (var (harder, easier, weight) in answers.OrderBy(_ => rng.Next()))
            {
                double p = 1 / (1 + Math.Exp(-Sharpness * (Score(harder) - Score(easier))));
                double g = lr * (1 - p) * weight * Sharpness;
                if (Parse(harder) is { } h) log[h.Id] = log.GetValueOrDefault(h.Id) + g;
                if (Parse(easier) is { } e) log[e.Id] = log.GetValueOrDefault(e.Id) - g;
            }
            foreach (var id in log.Keys.ToList()) log[id] = Math.Clamp(log[id] * 0.98, -Math.Log(8), Math.Log(8));
            lr *= 0.99;
        }
        foreach (var (id, l) in log) factors[id] = Math.Exp(l);
        return factors;
    }

    public static TuningLabView View(IReadOnlyList<OfferDefinition> offers, IReadOnlyList<StimMethod> methods, IReadOnlyList<TuningComparisonEntry> history,
        int? userId, double pointsPerMinute, Random random)
    {
        var modeHistory = history.Where(x => x.Mode == Mode).ToList();
        var deals = Candidates(offers);
        // Method assignments anchor the scale: the timed ones, in points.
        var anchors = TuningLab.Candidates(TuningLab.Stim, methods, []).Where(x => x.Group == "Timed").ToList();
        var points = deals.Concat(anchors).GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().Points);
        var factors = Fit(points, modeHistory);
        var wins = modeHistory.GroupBy(x => x.WinnerKey).ToDictionary(g => g.Key, g => g.Count());
        var losses = modeHistory.GroupBy(x => x.LoserKey).ToDictionary(g => g.Key, g => g.Count());
        LabCandidate Scored(LabCandidate c) => c with
        {
            Points = c.Points * (Parse(c.Key) is { } k ? factors.GetValueOrDefault(k.Id, 1) : 1),
            Wins = wins.GetValueOrDefault(c.Key), Losses = losses.GetValueOrDefault(c.Key),
        };
        deals = deals.Select(Scored).ToList();
        anchors = anchors.Select(Scored).ToList();

        double Harder(LabCandidate a, LabCandidate b) => 1 / (1 + Math.Exp(-Sharpness * (Math.Log(Math.Max(0.5, a.Points)) - Math.Log(Math.Max(0.5, b.Points)))));
        double Impact(LabCandidate a, LabCandidate b)
        {
            if (Parse(a.Key) is { } x && Parse(b.Key) is { } y && x.Id == y.Id) return double.NegativeInfinity; // same deal, bigger amount: obvious
            double p = Harder(a, b);
            return (1 - Math.Abs(2 * p - 1)) * (1 + 0.65 * (1 / Math.Sqrt(a.Wins + a.Losses + 1.0) + 1 / Math.Sqrt(b.Wins + b.Losses + 1.0)));
        }
        (LabCandidate, LabCandidate)? best = null;
        double bestImpact = double.NegativeInfinity;
        bool anchor = anchors.Count > 0 && random.NextDouble() < 0.25;
        for (int i = 0; i < 1500 && deals.Count > 1; i++)
        {
            var a = deals[random.Next(deals.Count)];
            var b = anchor ? anchors[random.Next(anchors.Count)] : deals[random.Next(deals.Count)];
            if (a.Key == b.Key) continue;
            double impact = Impact(a, b);
            if (impact > bestImpact) (bestImpact, best) = (impact, (a, b));
        }
        LabSide? left = null, right = null;
        if (best is { } pair)
        {
            if (random.Next(2) == 0) pair = (pair.Item2, pair.Item1);
            double p = Harder(pair.Item1, pair.Item2);
            (left, right) = (new LabSide(pair.Item1, p), new LabSide(pair.Item2, 1 - p));
        }

        var rows = offers.Where(x => x.Enabled).Select(d =>
        {
            int hold = d.MaxHold > 0 ? (d.MinHold + d.MaxHold) / 2 : 0;
            double f = factors.GetValueOrDefault(d.Id, 1);
            double low = Value(d, d.MinAmount, hold), high = Value(d, d.MaxAmount, hold);
            int seen = modeHistory.Count(x => x.WinnerKey.StartsWith($"offer:{d.Id}:") || x.LoserKey.StartsWith($"offer:{d.Id}:"));
            return new LabDealRow(d.Id, d.Name, Kind(d.Kind), low, high, low * f, high * f, f, seen);
        }).ToList();

        return new(Mode, modeHistory.Count, modeHistory.Count(x => x.UserId == userId), 0, left, right, [], [], 0, pointsPerMinute) { Deals = rows };
    }
}
