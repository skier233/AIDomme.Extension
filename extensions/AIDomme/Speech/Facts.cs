using System.Globalization;

namespace AIDomme.Speech;

/// <summary>
/// What she knows at the moment she speaks, as named values that line files can test and use:
/// numbers, words, yes/no, and lists (tags on screen, performers). Built from the session state,
/// her recall of past sessions, her persona for the session and what is on screen.
/// </summary>
public sealed class Facts
{
    private readonly Dictionary<string, object> values = new(StringComparer.OrdinalIgnoreCase);

    public Facts Set(string key, object? value)
    {
        if (value is null) values.Remove(key);
        else values[key] = value;
        return this;
    }

    public bool TryGet(string key, out object value) => values.TryGetValue(key, out value!);

    /// <summary>These facts plus an utterance's own values (numbers where they parse), which take precedence.</summary>
    public Facts With(IReadOnlyDictionary<string, string> slots)
    {
        var copy = new Facts();
        foreach (var (key, value) in values) copy.values[key] = value;
        foreach (var (key, value) in slots)
            copy.values[key] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : value;
        return copy;
    }

    public IEnumerable<KeyValuePair<string, object>> All => values;

    /// <summary>The value as text for a slot, or null when she does not know it.</summary>
    public string? Text(string key)
    {
        if (!values.TryGetValue(key, out var v)) return null;
        return v switch
        {
            string s => s.Length == 0 ? null : s,
            IReadOnlyList<string> list => list.Count == 0 ? null : list[0],
            bool b => b ? "yes" : null,
            double d => d.ToString("0.##", CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        };
    }

    public double Number(string key)
        => values.TryGetValue(key, out var v) ? v switch
        {
            double d => d,
            int i => i,
            bool b => b ? 1 : 0,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            IReadOnlyList<string> list => list.Count,
            _ => 0,
        } : 0;

    public bool Truthy(string key)
        => values.TryGetValue(key, out var v) && v switch
        {
            bool b => b,
            double d => d != 0,
            int i => i != 0,
            string s => s.Length > 0,
            IReadOnlyList<string> list => list.Count > 0,
            _ => true,
        };

    public IEnumerable<string> Words(string key)
        => !values.TryGetValue(key, out var v) ? [] : v switch
        {
            IReadOnlyList<string> list => list,
            string s => [s],
            bool b => b ? ["true"] : ["false"],
            _ => [Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""],
        };
}

/// <summary>
/// The condition language of line files. Each condition is one of:
/// <c>key</c> (known and not zero/empty), <c>!key</c>, <c>key=a|b</c>, <c>key!=a|b</c>
/// (lists match when any element matches), <c>key&gt;=n</c>, <c>key&lt;=n</c>, <c>key&gt;n</c>,
/// <c>key&lt;n</c>, or <c>#tag</c> / <c>#a|b</c> / <c>!#tag</c> for the utterance's own tags. All must hold.
/// </summary>
public static class Conditions
{
    private static readonly string[] Operators = [">=", "<=", "!=", ">", "<", "="];

    public static bool Hold(IReadOnlyList<string> conditions, Facts facts, Utterance? u)
    {
        foreach (var condition in conditions)
            if (!HoldsOne(condition, facts, u)) return false;
        return true;
    }

    /// <summary>Throws when a condition cannot be parsed; used by the line-file validator.</summary>
    public static void Validate(string condition)
    {
        var c = condition.Trim();
        if (c.Length == 0) throw new FormatException("Empty condition.");
        if (c.StartsWith('!')) c = c[1..];
        if (c.StartsWith('#')) { if (c.Length < 2) throw new FormatException($"Empty tag in '{condition}'."); return; }
        var (key, op, value) = Split(c);
        if (key.Length == 0 || key.Any(char.IsWhiteSpace)) throw new FormatException($"Bad key in '{condition}'.");
        if (op is ">=" or "<=" or ">" or "<" && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            throw new FormatException($"'{condition}' compares with something that is not a number.");
        if (op is "=" or "!=" && value.Length == 0) throw new FormatException($"'{condition}' has no value.");
    }

    private static bool HoldsOne(string condition, Facts facts, Utterance? u)
    {
        var c = condition.Trim();
        bool negate = false;
        if (c.StartsWith('!') && !c.StartsWith("!="))
        {
            negate = true;
            c = c[1..];
        }
        if (c.StartsWith('#'))
        {
            // #a|b: the utterance has any of the tags.
            bool has = u is not null && c[1..].Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Any(tag => u.Has(tag.TrimStart('#')));
            return negate ? !has : has;
        }
        var (key, op, value) = Split(c);
        bool result = op switch
        {
            "" => facts.Truthy(key),
            "=" => Matches(facts, key, value),
            "!=" => !Matches(facts, key, value),
            _ => Compare(facts.Number(key), op, double.Parse(value, CultureInfo.InvariantCulture)) && facts.TryGet(key, out _),
        };
        return negate ? !result : result;
    }

    private static bool Matches(Facts facts, string key, string value)
    {
        var wanted = value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return facts.Words(key).Any(w => wanted.Any(x => string.Equals(x, w, StringComparison.OrdinalIgnoreCase)));
    }

    private static bool Compare(double actual, string op, double value) => op switch
    {
        ">=" => actual >= value,
        "<=" => actual <= value,
        ">" => actual > value,
        _ => actual < value,
    };

    private static (string Key, string Op, string Value) Split(string c)
    {
        foreach (var op in Operators)
        {
            int at = c.IndexOf(op, StringComparison.Ordinal);
            if (at > 0) return (c[..at].Trim(), op, c[(at + op.Length)..].Trim());
        }
        return (c, "", "");
    }
}
