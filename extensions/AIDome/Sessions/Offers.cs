using AIDome.Data;

namespace AIDome.Sessions;

/// <summary>The price an offer asks.</summary>
public enum OfferKind
{
    /// <summary>Edges (and optionally a hold) right now, before she grants it.</summary>
    EdgesNow,
    /// <summary>Edges owed before the next orgasm.</summary>
    EdgesLater,
    /// <summary>Days of denial.</summary>
    Denial,
    /// <summary>Punishment sessions owed.</summary>
    PunishmentSession,
    /// <summary>A coming session ends with a punishment ending.</summary>
    PunishmentEnding,
    /// <summary>The next orgasms are ruined.</summary>
    Ruin,
    /// <summary>The orgasm being asked for is ruined.</summary>
    RuinNow,
}

public enum OfferFlavor { Strict, Cruel, Playful, Teasing, Humiliating }

/// <summary>What the user wants from her.</summary>
public enum OfferBuys { Orgasm, Rest, Mercy }

/// <summary>An offer definition as a session sees it: snapshotted at the start, like methods.</summary>
public sealed record OfferTemplate
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    /// <summary>Her words. Slots: [amount], [hold], [s] (plural of amount), [what] (what is bought).</summary>
    public string Wording { get; init; } = "";
    public OfferKind Kind { get; init; }
    public int MinAmount { get; init; }
    public int MaxAmount { get; init; }
    public int MinHold { get; init; }
    public int MaxHold { get; init; }
    public double BasePoints { get; init; }
    public double PointsPerUnit { get; init; }
    public double PointsPerHoldSecond { get; init; }
    public OfferFlavor Flavor { get; init; }
    public DommeMood[] Moods { get; init; } = [];
    public bool ForOrgasm { get; init; }
    public bool ForRest { get; init; }
    public bool ForMercy { get; init; }

    public bool Buys(OfferBuys buys) => buys switch { OfferBuys.Orgasm => ForOrgasm, OfferBuys.Rest => ForRest, _ => ForMercy };

    public static OfferTemplate From(OfferDefinition d) => new()
    {
        Id = d.Id, Name = d.Name, Wording = d.Wording, Kind = d.Kind, MinAmount = d.MinAmount, MaxAmount = d.MaxAmount,
        MinHold = d.MinHold, MaxHold = d.MaxHold, BasePoints = d.BasePoints, PointsPerUnit = d.PointsPerUnit,
        PointsPerHoldSecond = d.PointsPerHoldSecond, Flavor = d.Flavor, ForOrgasm = d.ForOrgasm, ForRest = d.ForRest, ForMercy = d.ForMercy,
        Moods = d.Moods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => Enum.TryParse<DommeMood>(x, true, out var mood) ? mood : (DommeMood?)null).OfType<DommeMood>().ToArray(),
    };
}

/// <summary>A concrete offer on the table: the parameters are fixed when she makes it.</summary>
public sealed class PendingOffer
{
    public Guid Id { get; set; }
    public int TemplateId { get; set; }
    public string Name { get; set; } = "";
    public OfferBuys Buys { get; set; }
    public OfferKind Kind { get; set; }
    public int Amount { get; set; }
    public int Hold { get; set; }
    public int Severity { get; set; }
    public double Points { get; set; }
    public OfferFlavor Flavor { get; set; }
    public string Text { get; set; } = "";
    public DateTime ExpiresAtUtc { get; set; }
    /// <summary>For mercy: the game stakes the offer would replace.</summary>
    public List<Consequence> Replaces { get; set; } = [];

    /// <summary>The ledger entry accepting creates, if the price is paid later.</summary>
    public Stake? Stake => Kind switch
    {
        OfferKind.EdgesLater => new(LedgerKind.Edges, Amount, Severity),
        OfferKind.Denial => new(LedgerKind.Denial, Amount, Severity),
        OfferKind.PunishmentSession => new(LedgerKind.PunishmentSession, Amount, Severity),
        OfferKind.PunishmentEnding => new(LedgerKind.PunishmentEnding, Amount, Severity),
        OfferKind.Ruin => new(LedgerKind.Ruin, Amount, Severity),
        _ => null,
    };
}

/// <summary>Edges (and a hold) being paid right now for something she promised.</summary>
public sealed class Price
{
    public Guid OfferId { get; set; }
    public int EdgesLeft { get; set; }
    public int Hold { get; set; }
    public OfferBuys Reward { get; set; }
}

/// <summary>
/// Her deals. What the user asks for is valued in session points, so a deal costs the same in
/// every mode; she then picks an offer her mood and meanness favour, and fills in its parameters
/// to roughly match that value.
/// </summary>
public static class Offers
{
    /// <summary>How long an offer stays on the table.</summary>
    public const int ExpirySeconds = 60;

    /// <summary>What the thing asked for is worth, in session points.</summary>
    public static double Value(SessionState s, OfferBuys buys, IReadOnlyList<Consequence>? mercy = null)
    {
        switch (buys)
        {
            case OfferBuys.Orgasm:
            {
                double value = s.Budget * 0.35 + Math.Max(0, s.Budget - s.Points) * 0.6;
                value *= 0.6 + s.Denial / 10.0;
                return value * s.Mood.Mood switch { DommeMood.Cruel => 1.2, DommeMood.Playful => 0.85, _ => 1.0 };
            }
            case OfferBuys.Rest:
                return 8 + s.Arousal / 8;
            default:
                double total = 0;
                foreach (var c in mercy ?? [])
                    total += c.Kind switch
                    {
                        ConsequenceKind.ExtraEdges => c.Amount * (4 + c.Hold * 0.4),
                        ConsequenceKind.Torment => c.Amount * 0.25,
                        ConsequenceKind.Points => Math.Abs(Math.Min(0, c.Amount)),
                        ConsequenceKind.PermissionOdds => Math.Abs(Math.Min(0, c.Amount)) * 1.5,
                        _ => 0,
                    };
                return total;
        }
    }

    /// <summary>Picks and fills an offer for what the user wants, or null if nothing in the library fits.</summary>
    public static PendingOffer? Make(SessionState s, OfferBuys buys, IRandom r, DateTime now, IReadOnlyList<Consequence>? mercy = null)
    {
        double value = Value(s, buys, mercy);
        if (value <= 0) return null;
        var candidates = new List<(OfferTemplate, double)>();
        foreach (var t in s.OfferTemplates)
        {
            if (!t.Buys(buys) || t.PointsPerUnit <= 0 && t.BasePoints <= 0) continue;
            // Paying edges now for mercy from edges is no mercy; a ruined "now" only applies to an orgasm.
            if (buys == OfferBuys.Mercy && t.Kind == OfferKind.EdgesNow) continue;
            if (buys != OfferBuys.Orgasm && t.Kind == OfferKind.RuinNow) continue;
            double w = 1;
            if (t.Moods.Contains(s.Mood.Mood)) w *= 2.5;
            w *= t.Flavor switch
            {
                OfferFlavor.Cruel or OfferFlavor.Humiliating => 0.4 + s.Mean / 10.0,
                OfferFlavor.Playful or OfferFlavor.Teasing => Math.Max(0.2, 1.4 - s.Mean / 15.0),
                _ => 1,
            };
            var (low, high) = Range(t);
            if (value < low * 0.8 || value > high * 1.25) w *= 0.15;
            if (s.RecentOfferTemplates.Contains(t.Id)) w *= 0.3;
            candidates.Add((t, w));
        }
        if (candidates.Count == 0) return null;
        var template = r.Weighted(candidates, candidates[0].Item1);
        var (amount, hold) = Fill(template, value, r);
        double points = template.BasePoints + amount * (template.PointsPerUnit + hold * template.PointsPerHoldSecond);
        s.RecentOfferTemplates.Add(template.Id);
        if (s.RecentOfferTemplates.Count > 4) s.RecentOfferTemplates.RemoveAt(0);
        return new()
        {
            Id = Guid.NewGuid(), TemplateId = template.Id, Name = template.Name, Buys = buys, Kind = template.Kind, Amount = amount,
            Hold = hold, Points = Math.Round(points, 1), Flavor = template.Flavor, Severity = Severity(template.Kind, amount),
            Text = Words(template, amount, hold, buys), ExpiresAtUtc = now.AddSeconds(ExpirySeconds), Replaces = mercy?.ToList() ?? [],
        };
    }

    /// <summary>The value range an offer can reach with its parameters.</summary>
    public static (double Low, double High) Range(OfferTemplate t)
    {
        double low = t.BasePoints + t.MinAmount * (t.PointsPerUnit + t.MinHold * t.PointsPerHoldSecond);
        double high = t.BasePoints + t.MaxAmount * (t.PointsPerUnit + t.MaxHold * t.PointsPerHoldSecond);
        return (low, high);
    }

    public static (int Amount, int Hold) Fill(OfferTemplate t, double value, IRandom r)
    {
        int hold = t.MaxHold > t.MinHold ? r.Next(t.MinHold, t.MaxHold + 1) : t.MinHold;
        double perUnit = t.PointsPerUnit + hold * t.PointsPerHoldSecond;
        int amount = perUnit <= 0 ? t.MinAmount : (int)Math.Round((value - t.BasePoints) / perUnit * (0.9 + r.NextDouble() * 0.2));
        return (Math.Clamp(amount, Math.Max(1, t.MinAmount), Math.Max(1, Math.Max(t.MinAmount, t.MaxAmount))), hold);
    }

    public static int Severity(OfferKind kind, int amount) => Math.Clamp(kind switch
    {
        OfferKind.Denial => 2 + amount,
        OfferKind.PunishmentSession => 4 + amount,
        OfferKind.PunishmentEnding => 6,
        OfferKind.Ruin => 3 + amount,
        OfferKind.EdgesLater => 2 + amount / 10,
        _ => 2,
    }, 1, 10);

    public static string Words(OfferTemplate t, int amount, int hold, OfferBuys buys)
    {
        string what = buys switch { OfferBuys.Orgasm => "cum", OfferBuys.Rest => "rest", _ => "skip your punishment" };
        return t.Wording.Replace("[amount]", amount.ToString()).Replace("[hold]", hold.ToString())
            .Replace("[s]", amount == 1 ? "" : "s").Replace("[what]", what);
    }
}
