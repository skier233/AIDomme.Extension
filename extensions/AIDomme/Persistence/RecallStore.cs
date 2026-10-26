using System.Text.Json;
using AIDomme.Data;
using AIDomme.Sessions;
using AIDomme.Sessions.Plan;
using Microsoft.EntityFrameworkCore;

namespace AIDomme.Persistence;

/// <summary>
/// What she remembers and how the user set her up: recall of earlier sessions from the facts
/// already stored, the user's persona and segment settings, and which of her lines have been used.
/// </summary>
public static class RecallStore
{
    private static readonly JsonSerializerOptions Json = GuidedRecovery.Json;

    public static async Task<DommeSettings> SettingsAsync(GuidedData db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<string>($"""
            SELECT "Settings" AS "Value" FROM aidomme_domme_settings WHERE "UserId" = {db.UserId}
            """).ToListAsync(ct);
        if (rows.Count == 0) return new();
        try { return JsonSerializer.Deserialize<DommeSettings>(rows[0], Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static async Task SaveSettingsAsync(GuidedData db, DommeSettings settings, DateTime now, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(settings, Json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO aidomme_domme_settings ("UserId", "Settings", "UpdatedAtUtc") VALUES ({db.UserId}, {json}, {now})
            ON CONFLICT ("UserId") DO UPDATE SET "Settings" = EXCLUDED."Settings", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """, ct);
    }

    /// <summary>What she has learned about his body so far.</summary>
    public static async Task<LearnedProfile> LearnedAsync(GuidedData db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<string?>($"""
            SELECT "Learned" AS "Value" FROM aidomme_domme_settings WHERE "UserId" = {db.UserId}
            """).ToListAsync(ct);
        if (rows.Count == 0 || rows[0] is null) return new();
        try { return JsonSerializer.Deserialize<LearnedProfile>(rows[0]!, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Saves what she has learned (null forgets it). His own settings are not touched.</summary>
    public static async Task SaveLearnedAsync(GuidedData db, LearnedProfile? learned, DateTime now, CancellationToken ct)
    {
        string? json = learned is null ? null : JsonSerializer.Serialize(learned, Json);
        string settings = JsonSerializer.Serialize(new DommeSettings(), Json);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO aidomme_domme_settings ("UserId", "Settings", "Learned", "UpdatedAtUtc") VALUES ({db.UserId}, {settings}, {json}, {now})
            ON CONFLICT ("UserId") DO UPDATE SET "Learned" = EXCLUDED."Learned"
            """, ct);
    }

    /// <summary>His profile as her next session would start with it.</summary>
    public static async Task<ProfileState> ProfileAsync(GuidedData db, CancellationToken ct)
        => ProfileState.From((await SettingsAsync(db, ct)).Profile, await LearnedAsync(db, ct));

    /// <summary>Records lines with cross-session cooldowns so they stay rare.</summary>
    public static async Task RecordUsageAsync(GuidedData db, IReadOnlyCollection<string> modules, int? sessionId, DateTime now, CancellationToken ct)
    {
        foreach (var module in modules)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_narration_usage ("UserId", "ModuleId", "SessionId", "UsedAtUtc") VALUES ({db.UserId}, {module}, {sessionId}, {now})
                """, ct);
    }

    /// <summary>His pace: points per minute of play over his last finished sessions of five minutes or more, newest first.</summary>
    public static (double? PointsPerMinute, int Sessions) Pace(IEnumerable<(SessionOutcome Outcome, int? ActiveSeconds, int Points)> newestFirst)
    {
        var paced = newestFirst.Where(x => x.Outcome is not (SessionOutcome.Unknown or SessionOutcome.Abandoned) && x.ActiveSeconds >= 300 && x.Points > 0)
            .Take(10).ToList();
        return paced.Count == 0 ? (null, 0) : (paced.Sum(x => x.Points) / (paced.Sum(x => x.ActiveSeconds!.Value) / 60.0), paced.Count);
    }

    /// <summary>His current points per minute, as the session budget uses it.</summary>
    public static async Task<double> PaceAsync(GuidedData db, CancellationToken ct)
    {
        var rows = await db.Sessions.AsNoTracking().Where(x => !x.IsDebugSession && x.Mode != "Homework")
            .OrderByDescending(x => x.StartedAtUtc).Take(30).Select(x => new { x.Outcome, x.ActiveSeconds, x.Points }).ToListAsync(ct);
        var (pace, count) = Pace(rows.Select(x => (x.Outcome, x.ActiveSeconds, x.Points)));
        return Sessions.Points.Pace(new Recall { PointsPerMinute = pace, PaceSessions = count });
    }

    public static async Task<Recall> RecallAsync(GuidedData db, TrackerDecisionMetrics metrics, DateTime now, CancellationToken ct)
    {
        var sessions = await db.Sessions.AsNoTracking()
            .Where(x => !x.IsDebugSession && x.Mode != "Homework")
            .OrderByDescending(x => x.StartedAtUtc).Take(30)
            .Select(x => new { x.Id, x.StartedAtUtc, x.LastInteractionUtc, x.Outcome, x.ContractJson, x.PlanJson, x.Mode, x.Points, x.ActiveSeconds })
            .ToListAsync(ct);
        var ids = sessions.Select(x => x.Id).ToList();
        var requests = await db.CumRequests.AsNoTracking().Where(x => ids.Contains(x.SessionId))
            .GroupBy(x => x.SessionId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        var finished = sessions.Where(x => x.Outcome != SessionOutcome.Unknown).ToList();
        int streak = 0;
        foreach (var session in finished)
        {
            if (session.Outcome != SessionOutcome.Denied) break;
            streak++;
        }
        var recent = now.AddDays(-30);
        int walkouts = sessions.Count(x => x.Outcome == SessionOutcome.Abandoned && x.StartedAtUtc > recent);
        int broken = await db.Commitments.CountAsync(x => x.Status == CommitmentStatus.Broken && x.ResolvedAtUtc > recent, ct);

        // What he edges on most, in sessions where she chose the method.
        var guidedIds = sessions.Where(x => x.Mode is "FullGuided" or "OwnContent").Select(x => x.Id).ToList();
        var favorites = await db.Edges.AsNoTracking().Where(x => guidedIds.Contains(x.SessionId))
            .GroupBy(x => x.StimMethodId).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(3).ToListAsync(ct);
        var methods = await db.MethodsAsync(ct);
        var favoriteIds = favorites.Where(x => x.Count >= 3).Select(x => x.Key).ToArray();

        SessionPersona? lastPersona = null;
        if (sessions.FirstOrDefault()?.ContractJson is { } contractJson)
        {
            try { lastPersona = JsonSerializer.Deserialize<SessionContract>(contractJson, Json)?.Persona; }
            catch (JsonException) { }
        }
        var openings = new List<string>();
        string[] lastSegments = [];
        foreach (var session in sessions.Take(5))
        {
            if (session.PlanJson is null) continue;
            try
            {
                var plan = JsonSerializer.Deserialize<SessionPlan>(session.PlanJson, Json);
                var types = plan?.Segments.Select(x => x.Type).Where(x => x != SegmentCatalogue.Ending).ToArray() ?? [];
                if (types.Length > 0) openings.Add(types[0]);
                if (session == sessions[0]) lastSegments = types.Distinct().ToArray();
            }
            catch (JsonException) { }
        }

        var usage = await db.Database.SqlQuery<UsageRow>($"""
            SELECT "ModuleId", COUNT(*)::integer AS "Uses", MAX("UsedAtUtc") AS "LastUsedUtc"
            FROM aidomme_narration_usage WHERE "UserId" = {db.UserId} GROUP BY "ModuleId"
            """).ToListAsync(ct);
        var starts = sessions.Select(x => x.StartedAtUtc).ToList();

        var (pace, pacedCount) = Pace(sessions.Select(x => (x.Outcome, x.ActiveSeconds, x.Points)));

        return new()
        {
            PointsPerMinute = pace, PaceSessions = pacedCount,
            Sessions = sessions.Count,
            DaysSinceLastSession = sessions.FirstOrDefault() is { } last ? (now - last.LastInteractionUtc).TotalDays : null,
            LastOutcome = finished.FirstOrDefault()?.Outcome,
            DenialStreak = streak,
            DaysSinceOrgasm = metrics.LastOrgasmAtUtc is DateTime orgasm ? (now - orgasm).TotalDays : null,
            EdgesToday = metrics.EdgesToday,
            EdgesSinceOrgasm = metrics.EdgesSinceLastOrgasm,
            LastSessionBegs = sessions.FirstOrDefault() is { } previous && requests.TryGetValue(previous.Id, out var begs) ? begs : 0,
            AverageBegs = sessions.Count == 0 ? 0 : sessions.Average(x => requests.TryGetValue(x.Id, out var n) ? n : 0),
            WalkoutsRecently = walkouts,
            BrokenPromisesRecently = broken,
            FavoriteMethods = favoriteIds,
            FavoriteMethodName = favoriteIds.Length > 0 ? methods.FirstOrDefault(x => x.Id == favoriteIds[0])?.Name : null,
            RecentOpenings = openings.ToArray(),
            LastSegments = lastSegments,
            LastPersona = lastPersona,
            Usage = usage.ToDictionary(x => x.ModuleId,
                x => new ModuleUse(x.Uses, x.LastUsedUtc, starts.Count(start => start > x.LastUsedUtc)), StringComparer.OrdinalIgnoreCase),
        };
    }

    public sealed class UsageRow
    {
        public string ModuleId { get; set; } = "";
        public int Uses { get; set; }
        public DateTime LastUsedUtc { get; set; }
    }
}
