using System.Text.Json;
using AIDome.Data;
using AIDome.Persistence;
using AIDome.Sessions;
using Npgsql;

namespace AIDome.Tests;

/// <summary>
/// One account's AI Domme settings read from a real Cove database, so simulated transcripts can run
/// with them instead of the defaults: her persona, endings, session lengths and segment preferences,
/// the effective method library (overrides applied, removed methods left out), deals and punishment
/// endings, the defaults the setup screen would suggest, and the user's pace.
/// <para>
/// Set <c>AIDOMME_SETTINGS_FROM</c> to a connection string (for example
/// <c>Host=127.0.0.1;Port=5430;Username=postgres;Database=cove</c>) and optionally
/// <c>AIDOMME_SETTINGS_USER</c> to a user id. The connection is read-only: every transaction is opened
/// with <c>default_transaction_read_only</c>, so nothing in that database can change.
/// </para>
/// </summary>
public sealed record RealSettings(
    int UserId,
    DommeSettings Settings,
    List<MethodInfo> Methods,
    List<OfferTemplate> Offers,
    List<EnderInfo> Enders,
    SessionPreferences Defaults,
    Recall Recall)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private static readonly Lazy<RealSettings?> Loaded = new(() => LoadAsync().GetAwaiter().GetResult());

    /// <summary>The settings named by the environment, or null when none are configured.</summary>
    public static RealSettings? FromEnvironment => Loaded.Value;

    private static async Task<RealSettings?> LoadAsync()
    {
        var source = Environment.GetEnvironmentVariable("AIDOMME_SETTINGS_FROM");
        if (string.IsNullOrWhiteSpace(source)) return null;
        var builder = new NpgsqlConnectionStringBuilder(source) { Options = "-c default_transaction_read_only=on" };
        await using var db = new NpgsqlConnection(builder.ConnectionString);
        await db.OpenAsync();

        int user = int.TryParse(Environment.GetEnvironmentVariable("AIDOMME_SETTINGS_USER"), out var u) ? u
            : await ScalarAsync<int?>(db, """SELECT MIN("UserId") FROM aidomme_domme_settings""") ?? 1;

        var settingsJson = await ScalarAsync<string?>(db, """SELECT "Settings" FROM aidomme_domme_settings WHERE "UserId" = @u""", user);
        var settings = (settingsJson is null ? new DommeSettings() : JsonSerializer.Deserialize<DommeSettings>(settingsJson, Json) ?? new())
            .Validate();

        bool removable = await ScalarAsync<long>(db,
            """SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'aidomme_methods' AND column_name = 'Removed'""") > 0;
        var methods = new List<StimMethod>();
        await foreach (var r in RowsAsync(db, $"""
            SELECT "Id", "UserId", "BaseId", "Name", "Intensity", "Tease", "TortLevel", "Closeness", "RequiresLube", "ToyCategory",
                   "Description", "TimePointMultiplier", "EdgePointMultiplier", {(removable ? "\"Removed\"" : "FALSE")}
            FROM aidomme_methods WHERE "UserId" IS NULL OR "UserId" = @u
            """, user))
            methods.Add(new()
            {
                Id = r.GetInt32(0), UserId = r.IsDBNull(1) ? null : r.GetInt32(1), BaseId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Name = r.GetString(3), Intensity = r.GetInt32(4), Tease = r.GetInt32(5), TortLevel = r.GetInt32(6), Closeness = r.GetInt32(7),
                RequiresLube = r.GetBoolean(8), ToyCategory = (ToyCategory)r.GetInt32(9), Description = r.GetString(10),
                TimePointMultiplier = r.GetDouble(11), EdgePointMultiplier = r.GetDouble(12), Removed = r.GetBoolean(13),
            });

        var enders = new List<PunishmentEnder>();
        await foreach (var r in RowsAsync(db, $"""
            SELECT "Id", "UserId", "BaseId", "Name", "Severity", "Anticipation", "Humiliation", "RequiresBondage", "RequiresCEI",
                   "RequiresSelfOral", "InstructionText", "AnnouncementText", "EndsInOrgasm", {(removable ? "\"Removed\"" : "FALSE")}
            FROM aidomme_enders WHERE "UserId" IS NULL OR "UserId" = @u
            """, user))
            enders.Add(new()
            {
                Id = r.GetInt32(0), UserId = r.IsDBNull(1) ? null : r.GetInt32(1), BaseId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Name = r.GetString(3), Severity = r.GetInt32(4), Anticipation = r.GetInt32(5), Humiliation = r.GetInt32(6),
                RequiresBondage = r.GetBoolean(7), RequiresCEI = r.GetBoolean(8), RequiresSelfOral = r.GetBoolean(9),
                InstructionText = r.GetString(10), AnnouncementText = r.GetString(11), EndsInOrgasm = r.GetBoolean(12), Removed = r.GetBoolean(13),
            });

        var offers = new List<OfferDefinition>();
        await foreach (var r in RowsAsync(db, """
            SELECT "Id", "UserId", "BaseId", "Name", "Wording", "Kind", "MinAmount", "MaxAmount", "MinHold", "MaxHold", "BasePoints",
                   "PointsPerUnit", "PointsPerHoldSecond", "Flavor", "Moods", "ForOrgasm", "ForRest", "ForMercy", "Enabled"
            FROM aidomme_offer_definitions WHERE "UserId" IS NULL OR "UserId" = @u
            """, user))
            offers.Add(new()
            {
                Id = r.GetInt32(0), UserId = r.IsDBNull(1) ? null : r.GetInt32(1), BaseId = r.IsDBNull(2) ? null : r.GetInt32(2),
                Name = r.GetString(3), Wording = r.GetString(4), Kind = Enum.Parse<OfferKind>(r.GetString(5)), MinAmount = r.GetInt32(6),
                MaxAmount = r.GetInt32(7), MinHold = r.GetInt32(8), MaxHold = r.GetInt32(9), BasePoints = r.GetDouble(10),
                PointsPerUnit = r.GetDouble(11), PointsPerHoldSecond = r.GetDouble(12), Flavor = Enum.Parse<OfferFlavor>(r.GetString(13)),
                Moods = r.GetString(14), ForOrgasm = r.GetBoolean(15), ForRest = r.GetBoolean(16), ForMercy = r.GetBoolean(17), Enabled = r.GetBoolean(18),
            });

        // What the setup screen would suggest (his last sessions), and his pace.
        var recent = new List<(int Intensity, int Length, int Denial, int Mean, SessionOutcome Outcome, int? Active, int Points)>();
        bool paced = await ScalarAsync<long>(db,
            """SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'aidomme_sessions' AND column_name = 'ActiveSeconds'""") > 0;
        await foreach (var r in RowsAsync(db, $"""
            SELECT "Intensity", "Length", "Denial", "Mean", "Outcome", {(paced ? "\"ActiveSeconds\"" : "NULL::integer")}, "Points"
            FROM aidomme_sessions WHERE "UserId" = @u AND NOT "IsDebugSession" AND COALESCE("Mode", '') <> 'Homework'
            ORDER BY "StartedAtUtc" DESC LIMIT 30
            """, user))
            recent.Add((r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), Outcome(r.GetValue(4)), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetInt32(6)));
        static int Avg(IEnumerable<int> values) => Math.Clamp((int)Math.Round(values.Select(v => v > 0 ? v : 5).DefaultIfEmpty(5).Average()), 1, 10);
        var last = recent.Take(5).ToList();
        var defaults = new SessionPreferences
        {
            Intensity = Avg(last.Select(x => x.Intensity)), Length = Avg(last.Select(x => x.Length)),
            Denial = Avg(last.Select(x => x.Denial)), Mean = Avg(last.Select(x => x.Mean)),
        };
        var (pace, count) = RecallStore.Pace(recent.Select(x => (x.Outcome, x.Active, x.Points)));

        return new(user, settings,
            Definitions.Resolve(methods).Where(x => !x.Removed).Select(MethodInfo.From).ToList(),
            Definitions.Resolve(offers).Where(x => x.Enabled).Select(OfferTemplate.From).ToList(),
            Definitions.Resolve(enders).Where(x => !x.Removed).Select(EnderInfo.From).ToList(),
            defaults,
            new Recall { Sessions = recent.Count, PointsPerMinute = pace, PaceSessions = count });
    }

    private static SessionOutcome Outcome(object value) => value switch
    {
        int i => (SessionOutcome)i,
        string s when Enum.TryParse<SessionOutcome>(s, out var o) => o,
        _ => SessionOutcome.Unknown,
    };

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection db, string sql, int? user = null)
    {
        await using var command = new NpgsqlCommand(sql, db);
        if (user is int id) command.Parameters.AddWithValue("u", id);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
    }

    private static async IAsyncEnumerable<NpgsqlDataReader> RowsAsync(NpgsqlConnection db, string sql, int user)
    {
        await using var command = new NpgsqlCommand(sql, db);
        command.Parameters.AddWithValue("u", user);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) yield return reader;
    }

    /// <summary>A short description for the top of each transcript.</summary>
    public string Describe()
        => $"Settings from user {UserId}: {Methods.Count} methods, {Offers.Count} deals, defaults intensity {Defaults.Intensity}, length {Defaults.Length}, " +
           $"denial {Defaults.Denial}, meanness {Defaults.Mean}; endings: denial {Settings.Endings.Denial}, ruins {Settings.Endings.Ruins}; " +
           $"length 1/5/10 = {Settings.Lengths.Length1}/{Settings.Lengths.Length5}/{Settings.Lengths.Length10} min; " +
           $"pace {Points.Pace(Recall):0.0} points a minute ({Recall.PaceSessions} sessions).";
}
