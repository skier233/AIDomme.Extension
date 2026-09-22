using System;
using AIDome2025.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDome2025.Services;

public interface IInteractionManager
{
    Task<EdgeEntry> RecordEdgeAsync(int speed, TimeSpan heldDuration, CancellationToken cancellationToken = default);
    Task<OrgasmEntry> RecordOrgasmAsync(bool allowed, CancellationToken cancellationToken = default);
    Task<SessionRecord> TouchSessionAsync(CancellationToken cancellationToken = default);
    Task<SessionRecord?> GetLatestSessionAsync(CancellationToken cancellationToken = default);
    Task<SessionRecord> StartSessionAsync(SessionParameters parameters, CancellationToken cancellationToken = default);
    Task<CumRequest> RecordCumRequestAsync(bool decision, CancellationToken cancellationToken = default);
    Task<int> GetCurrentSessionId(CancellationToken cancellationToken = default);
    Task<InfractionEntry> RecordInfractionAsync(InfractionType type, string? note, CancellationToken cancellationToken = default);
}

public sealed class InteractionManager : IInteractionManager
{
    private static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(15);
    private const int DefaultStimMethodId = 1;

    private readonly IDbContextFactory<CommandCenterDbContext> _dbContextFactory;
    private readonly Func<int?>? _activeSession;

    public InteractionManager(IDbContextFactory<CommandCenterDbContext> dbContextFactory, Func<int?>? activeSession = null)
    {
        _dbContextFactory = dbContextFactory;
        _activeSession = activeSession;
    }

    public async Task<EdgeEntry> RecordEdgeAsync(int speed, TimeSpan heldDuration, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);

        var entry = new EdgeEntry
        {
            TimestampUtc = now,
            Session = session,
            StimMethodId = DefaultStimMethodId,
            Speed = speed,
            HeldDuration = heldDuration
        };

        dbContext.Edges.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<int> GetCurrentSessionId(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session.Id;
    }

    public async Task<OrgasmEntry> RecordOrgasmAsync(bool allowed, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);

        var entry = new OrgasmEntry
        {
            TimestampUtc = now,
            Allowed = allowed,
            Session = session
        };

        dbContext.Orgasms.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<SessionRecord> TouchSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<SessionRecord?> GetLatestSessionAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (_activeSession?.Invoke() is int active)
            return await dbContext.Sessions.FirstOrDefaultAsync(x => x.Id == active, cancellationToken);
        var session = await dbContext.Sessions
            .OrderByDescending(s => s.LastInteractionUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return session;
    }

    public async Task<SessionRecord> StartSessionAsync(SessionParameters p, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var session = new SessionRecord
        {
            StartedAtUtc = now,
            LastInteractionUtc = now,
            Intensity = p.Intensity,
            Length = p.Length,
            Denial = p.Denial,
            Kink = p.Kink,
            Mean = p.Mean,
            AllowToys = p.AllowToys,
            AllowVibrator = p.AllowVibrator,
            AllowFleshlight = p.AllowFleshlight,
            AllowAutoblow = p.AllowAutoblow,
            AllowProstate = p.AllowProstate,
            AllowAnal = p.AllowAnal,
            AllowBondage = p.AllowBondage,
            AllowCEI = p.AllowCEI,
            AllowSelfOral = p.AllowSelfOral,
            IsDebugSession = p.IsDebugSession,
            // Disconnection is not evidence of an unreported orgasm.
            SuspectedUnauthorizedOrgasm = false,
        };

        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<InfractionEntry> RecordInfractionAsync(InfractionType type, string? note, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);

        var entry = new InfractionEntry
        {
            TimestampUtc = now,
            SessionId = session.Id,
            Type = type,
            Notes = note ?? string.Empty
        };

        dbContext.Infractions.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<CumRequest> RecordCumRequestAsync(bool decision, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var session = await GetBoundSessionAsync(dbContext, now, cancellationToken);

        var entry = new CumRequest
        {
            TimestampUtc = now,
            Session = session,
            Decision = decision
        };

        dbContext.CumRequests.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    private async Task<SessionRecord> GetBoundSessionAsync(CommandCenterDbContext db, DateTime now, CancellationToken ct)
    {
        if (_activeSession?.Invoke() is not int active) return await GetOrCreateSessionAsync(db, now, ct);
        var session = await db.Sessions.FirstOrDefaultAsync(x => x.Id == active, ct)
            ?? throw new InvalidOperationException("The active guided session no longer exists.");
        session.LastInteractionUtc = now;
        return session;
    }

    public static async Task<SessionRecord> GetOrCreateSessionAsync(CommandCenterDbContext dbContext, DateTime utcNow, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .OrderByDescending(s => s.LastInteractionUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (session is null || utcNow - session.LastInteractionUtc > SessionTimeout)
        {
            session = new SessionRecord
            {
                StartedAtUtc = utcNow,
                LastInteractionUtc = utcNow
            };

            dbContext.Sessions.Add(session);
        }
        else
        {
            session.LastInteractionUtc = utcNow;
        }

        return session;
    }
}
