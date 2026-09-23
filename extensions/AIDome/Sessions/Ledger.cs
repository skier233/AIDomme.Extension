namespace AIDome.Sessions;

/// <summary>What an owed ledger entry asks of the user.</summary>
public enum LedgerKind
{
    /// <summary>No orgasm until the window ends (Amount days).</summary>
    Denial,
    /// <summary>Amount sessions that must be punishment sessions.</summary>
    PunishmentSession,
    /// <summary>Amount sessions that end with a punishment ending from the library.</summary>
    PunishmentEnding,
    /// <summary>Amount edges to record before the next orgasm.</summary>
    Edges,
    /// <summary>The next Amount permitted orgasms are ruined.</summary>
    Ruin,
}

public enum LedgerStatus { Owed, Serving, Served }

/// <summary>Where an entry came from.</summary>
public enum LedgerOrigin { Rule, Commitment, Offer, Legacy }

/// <summary>A consequence to be written to the ledger.</summary>
public sealed record Stake(LedgerKind Kind, int Amount, int Severity)
{
    public string Describe() => Kind switch
    {
        LedgerKind.Denial => $"{Plural(Amount, "day")} of denial",
        LedgerKind.PunishmentSession => Amount == 1 ? "a punishment session" : $"{Amount} punishment sessions",
        LedgerKind.PunishmentEnding => Amount == 1 ? "a punishment ending" : $"{Amount} punishment endings",
        LedgerKind.Edges => $"{Plural(Amount, "edge")} before you cum again",
        _ => Amount == 1 ? "your next orgasm ruined" : $"your next {Amount} orgasms ruined",
    };

    public static string Describe(IEnumerable<Stake> stakes)
    {
        var parts = stakes.Select(x => x.Describe()).ToList();
        return parts.Count switch
        {
            0 => "nothing",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

    /// <summary>Same-kind stakes combined: amounts add up, the harsher severity wins.</summary>
    public static List<Stake> Merge(IEnumerable<Stake> stakes)
        => stakes.GroupBy(x => x.Kind)
            .Select(g => new Stake(g.Key, g.Sum(x => x.Amount), Math.Clamp(g.Max(x => x.Severity), 1, 10)))
            .OrderBy(x => x.Kind).ToList();

    public static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";
}

/// <summary>
/// The fixed rules for breaches the user did not agree stakes for in advance. A commitment's own
/// stakes apply when one was in force; these apply otherwise. Intentional breaches cost more.
/// </summary>
public static class BreachRules
{
    public static List<Stake> ForOrgasm(bool intentional, int mean, bool duringDenial)
    {
        var stakes = intentional
            ? new List<Stake> { new(LedgerKind.PunishmentSession, 1, Math.Min(10, 6 + mean / 3)), new(LedgerKind.Denial, 3, 6) }
            : [new(LedgerKind.Edges, 10 + mean, 3)];
        if (duringDenial) stakes.Add(intentional ? new(LedgerKind.PunishmentEnding, 1, 7) : new(LedgerKind.Denial, 2, 4));
        return Stake.Merge(stakes);
    }

    /// <summary>Walking out of a session she was running.</summary>
    public static List<Stake> ForQuit(int mean) => Stake.Merge([new(LedgerKind.PunishmentSession, 1, Math.Min(10, 5 + mean / 3)), new(LedgerKind.Edges, 10, 3)]);

    /// <summary>A broken commitment: the stakes the user accepted, and a punishment session on top when it was deliberate.</summary>
    public static List<Stake> ForCommitment(IEnumerable<Stake> agreed, bool intentional)
        => Stake.Merge(intentional ? agreed.Append(new(LedgerKind.PunishmentSession, 1, 7)) : agreed);
}

public enum CommitmentScope { Session, Days }

/// <summary>
/// What the user promises, picked from structured options: no orgasm and/or a session character,
/// for the next session or for a number of days. Free text is only a note.
/// </summary>
public sealed record CommitmentTerms
{
    public CommitmentScope Scope { get; init; } = CommitmentScope.Session;
    /// <summary>Length of the promise when the scope is days.</summary>
    public int Days { get; init; }
    public bool NoOrgasm { get; init; }
    public bool Long { get; init; }
    public bool Intense { get; init; }
    public bool Torturous { get; init; }
    public bool Mean { get; init; }
    public bool Teasing { get; init; }
    public string? Note { get; init; }

    public bool HasCharacter => Long || Intense || Torturous || Mean || Teasing;
    public SessionCharacter Character => new() { Long = Long, Intense = Intense, Torturous = Torturous, Mean = Mean, Teasing = Teasing };

    public CommitmentTerms Validate()
    {
        if (!Enum.IsDefined(Scope)) throw new ArgumentException("Unknown commitment scope.");
        if (!NoOrgasm && !HasCharacter) throw new ArgumentException("Promise her something: no orgasm, or what kind of session it will be.");
        if (Scope == CommitmentScope.Days && Days is < 1 or > 60) throw new ArgumentException("A promise for days runs from 1 to 60 days.");
        var note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim();
        if (note?.Length > 500) throw new ArgumentException("Notes are limited to 500 characters.");
        return this with { Days = Scope == CommitmentScope.Days ? Days : 0, Note = note };
    }

    public string Describe()
    {
        var character = new List<string>();
        if (Long) character.Add("long");
        if (Intense) character.Add("intense");
        if (Torturous) character.Add("torturous");
        if (Mean) character.Add("mean");
        if (Teasing) character.Add("teasing");
        string traits = character.Count switch
        {
            0 => "",
            1 => character[0],
            _ => string.Join(", ", character.Take(character.Count - 1)) + " and " + character[^1],
        };
        string days = $"the next {Stake.Plural(Days, "day")}";
        if (Scope == CommitmentScope.Session)
            return (NoOrgasm, traits.Length > 0) switch
            {
                (true, true) => $"no orgasm, and a {traits} session",
                (true, false) => "no orgasm this session",
                _ => $"a {traits} session",
            };
        return (NoOrgasm, traits.Length > 0) switch
        {
            (true, true) => $"no orgasm for {days}, and every session {traits} until then",
            (true, false) => $"no orgasm for {days}",
            _ => $"every session {traits} for {days}",
        };
    }
}

/// <summary>Her answer to a proposed commitment: the terms she holds the user to and what breaking them costs.</summary>
public sealed record CommitmentResponse(CommitmentTerms Terms, IReadOnlyList<Stake> Stakes, bool Countered, string Words);

/// <summary>
/// How she responds to a promise: she accepts or asks for more, and always names the price of
/// breaking it. A recent broken promise makes her harder to satisfy.
/// </summary>
public static class CommitmentRules
{
    public static CommitmentResponse Respond(CommitmentTerms proposed, int mean, int brokenRecently, IRandom r)
    {
        var asked = proposed.Validate();
        var terms = asked;
        bool distrust = brokenRecently > 0;
        if (distrust || r.Chance(20 + mean * 4))
        {
            if (terms.NoOrgasm && terms.Scope == CommitmentScope.Days)
            {
                double factor = distrust ? 1.5 : 1.25 + r.NextDouble() * 0.25;
                terms = terms with { Days = Math.Min(60, Math.Max(terms.Days + 1, (int)Math.Round(terms.Days * factor))) };
            }
            else terms = AddCharacter(terms, mean, r);
        }
        bool counter = terms != asked;

        int severity = Math.Clamp(3 + terms.Days / 3 + Count(terms) + (distrust ? 2 : 0), 1, 10);
        var stakes = new List<Stake>();
        if (terms.NoOrgasm)
            stakes.Add(new(LedgerKind.Denial, terms.Scope == CommitmentScope.Days ? Math.Max(2, (terms.Days + 1) / 2) : 2, severity));
        if (severity >= 6 || !terms.NoOrgasm) stakes.Add(new(LedgerKind.PunishmentSession, 1, severity));
        if (mean >= 7 || distrust) stakes.Add(new(LedgerKind.Ruin, 1, severity));
        stakes = Stake.Merge(stakes);

        string opener = distrust
            ? r.Pick(["After last time? Your word isn't worth much.", "You broke your last promise. So this one costs more."])
            : counter
                ? r.Pick(["That's not much of a promise.", "Cute. Not enough.", "I'll take it, with a change."])
                : r.Pick(["Good. I'll hold you to that.", "Agreed.", "Mmm. I like that."]);
        string what = counter ? $"Make it {terms.Describe()}." : $"{Capitalize(terms.Describe())}.";
        return new(terms, stakes, counter, $"{opener} {what} Break it and you owe me {Stake.Describe(stakes)}.");
    }

    private static CommitmentTerms AddCharacter(CommitmentTerms t, int mean, IRandom r)
    {
        var options = new List<(string, double)>();
        if (!t.Long) options.Add(("long", 3));
        if (!t.Mean) options.Add(("mean", mean >= 6 ? 4 : 1.5));
        if (!t.Intense) options.Add(("intense", 2));
        if (!t.Torturous && mean >= 5) options.Add(("torturous", 2));
        if (!t.Teasing && mean < 6) options.Add(("teasing", 2));
        if (!t.NoOrgasm && t.Scope == CommitmentScope.Session) options.Add(("no-orgasm", 1.5));
        return r.Weighted(options, "") switch
        {
            "long" => t with { Long = true },
            "mean" => t with { Mean = true },
            "intense" => t with { Intense = true },
            "torturous" => t with { Torturous = true },
            "teasing" => t with { Teasing = true },
            "no-orgasm" => t with { NoOrgasm = true },
            _ => t,
        };
    }

    private static int Count(CommitmentTerms t) => (t.Long ? 1 : 0) + (t.Intense ? 1 : 0) + (t.Torturous ? 1 : 0) + (t.Mean ? 1 : 0) + (t.Teasing ? 1 : 0);
    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
