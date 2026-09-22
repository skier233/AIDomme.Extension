using AIDome2025.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome2025.Services;

/// <summary>
/// Evaluates infractions, computes punishment tiers, and issues punishments.
/// 
/// Tier algorithm:
///   baseTier = infractionType.BaseSeverity
///   meanBoost = floor(session.Mean / 3)
///   recentInfractions = count infractions in last 7 days
///   escalation = max(0, recentInfractions - 1)
///   tier = clamp(baseTier + meanBoost + escalation, 1, 8)
/// </summary>
public sealed class PunishmentEngine : IPunishmentEngine
{
    private readonly IDbContextFactory<CommandCenterDbContext> _dbFactory;
    private readonly ILogger<PunishmentEngine> _logger;

    public PunishmentEngine(
        IDbContextFactory<CommandCenterDbContext> dbFactory,
        ILogger<PunishmentEngine> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PunishmentEntry>> ProcessInfractionsAsync(int sessionId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        var unprocessed = await db.Infractions
            .Where(i => i.SessionId == sessionId && !i.PunishmentAssigned)
            .ToListAsync(ct);

        if (unprocessed.Count == 0) return [];

        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        int recentCount = await db.Infractions
            .CountAsync(i => i.TimestampUtc >= sevenDaysAgo, ct);

        int mean = Math.Clamp(session.Mean > 0 ? session.Mean : 5, 1, 10);
        var results = new List<PunishmentEntry>();

        foreach (var infraction in unprocessed)
        {
            int baseTier = infraction.Type switch
            {
                InfractionType.UnauthorizedOrgasm => 4,
                InfractionType.RuinedOrgasm => 3,
                InfractionType.Disobedience => 2,
                InfractionType.GameFailure => 1,
                _ => 1
            };

            int meanBoost = mean / 3;
            int escalation = Math.Max(0, recentCount - 1);
            int tier = Math.Clamp(baseTier + meanBoost + escalation, 1, 8);

            var punishment = CreatePunishment(infraction, tier, session);
            db.Punishments.Add(punishment);
            infraction.PunishmentAssigned = true;
            results.Add(punishment);

            _logger.LogInformation(
                "Punishment issued: {Category} tier {Tier} for {Type} in session {SessionId}",
                punishment.Category, tier, infraction.Type, sessionId);
        }

        await db.SaveChangesAsync(ct);
        return results;
    }

    public async Task<IReadOnlyList<PunishmentEntry>> GetActivePunishmentsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.Punishments
            .Where(p => !p.Completed)
            .OrderByDescending(p => p.IssuedAtUtc)
            .ToListAsync(ct);
    }

    public async Task ServePunishmentCountAsync(int punishmentId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var punishment = await db.Punishments.FirstOrDefaultAsync(p => p.Id == punishmentId, ct);
        if (punishment is null) return;

        punishment.RemainingCount = Math.Max(0, punishment.RemainingCount - 1);
        if (punishment.RemainingCount <= 0)
        {
            punishment.Completed = true;
            punishment.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Punishment {Id} served. Remaining: {Remaining}", punishmentId, punishment.RemainingCount);
    }

    /// <summary>Select an eligible punishment ender for the given session settings.</summary>
    public async Task<PunishmentEnder> SelectPunishmentEnderAsync(int sessionId, int severityFloor, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var enders = await db.PunishmentEnders.ToListAsync(ct);

        bool allowBondage = session?.AllowBondage ?? false;
        bool allowCEI = session?.AllowCEI ?? false;

        return MethodSelector.PickEnder(enders, severityFloor, allowBondage, allowCEI, allowCEI);
    }

    private static PunishmentEntry CreatePunishment(InfractionEntry infraction, int tier, SessionRecord session)
    {
        var (category, description, count, denialDays) = tier switch
        {
            1 or 2 => (PunishmentCategory.Denial, $"No cumming for {tier + 1} days.", 0, tier + 1),
            3 or 4 => (PunishmentCategory.Denial, $"No cumming for {tier + 1} days. Edge {tier * 3} times daily.", 0, tier + 1),
            5 or 6 => (PunishmentCategory.CumMethod, $"Next {tier - 2} times you cum must use a punishment method.", tier - 2, 0),
            7 or 8 => (PunishmentCategory.Overload, $"Next {tier - 3} sessions are punishment sessions. Plus {tier + 2} days denial.", tier - 3, tier + 2),
            _ => (PunishmentCategory.Denial, "Denied for 3 days.", 0, 3)
        };

        return new PunishmentEntry
        {
            IssuedAtUtc = DateTime.UtcNow,
            SessionId = session.Id,
            Category = category,
            Tier = tier,
            Description = description,
            RemainingCount = count,
            DenialEndsAtUtc = denialDays > 0 ? DateTime.UtcNow.AddDays(denialDays) : null,
            Completed = false
        };
    }
}
