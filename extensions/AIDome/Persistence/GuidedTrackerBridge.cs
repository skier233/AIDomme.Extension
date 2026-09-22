using AIDome.Tracking;
using AIDome2025.Data;
using AIDome2025.Services;
using Cove.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome.Persistence;

public sealed class GuidedTrackerBridge(GuidedRuntimes runtimes, ILogger<GuidedTrackerBridge> logger)
{
    public async Task DrainUnderGateAsync(GuidedRuntime runtime, CancellationToken ct)
    {
        await using var db = await runtime.Factory.CreateDbContextAsync(ct);
        if (runtime.Online.HasActiveSession && await db.TrackerReceipts.AnyAsync(x => x.SessionId == runtime.Online.GetCurrentState().SessionId && x.VoidedAtUtc != null, ct))
            runtime.Commands.StopAfterTrackingCorrection();
        var pending = await db.TrackerReceipts.Where(x => x.DeliveredAtUtc == null && x.VoidedAtUtc == null).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var receipt in pending)
        {
            if (runtime.Online.HasActiveSession && !runtime.Online.GetCurrentState().IsPaused && runtime.Online.GetCurrentState().SessionId == receipt.SessionId)
            {
                try { await ConsumeAsync(runtime, db, receipt, ct); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Guided delivery failed for {EventId}; guidance stopped", receipt.EventId);
                    runtime.Commands.StopAfterTrackingCorrection();
                    await ClearSuspicionAsync(db, receipt.SessionId, ct);
                }
            }
            await runtime.SaveAsync(ct);
            receipt.DeliveredAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }
    public async Task RecordAsync(CoveContext tracker, TrackerEvent item, CancellationToken ct, RecordTrackerEvent? expected = null)
    {
        if (!runtimes.TryGet(item.UserId, out var runtime) || !runtime!.Online.HasActiveSession) return;
        await runtime.Gate.WaitAsync(ct);
        try
        {
            if (!runtime.Online.HasActiveSession || runtime.Online.GetCurrentState().IsPaused) return;
            var state = runtime.Online.GetCurrentState();
            if (expected?.GuidedSessionId is int sessionId && sessionId != state.SessionId
                || expected?.GuidedStepNumber is int stepNumber && stepNumber != state.StepNumber) return;
            await using var db = await runtime.Factory.InTrackerTransactionAsync(tracker, ct);
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == state.SessionId, ct)
                ?? throw new TrackerValidationException("The guided session changed. Refresh Cove before recording.");
            session.LastInteractionUtc = item.ReceivedAtUtc;
            var receipt = new GuidedEventReceipt { EventId = item.Id, Kind = item.Kind, SessionId = state.SessionId };
            if (item.Kind == "edge")
            {
                var parameters = runtime.Commands.GetTrackerEdgeParameters();
                var edge = new EdgeEntry { TimestampUtc = item.OccurredAtUtc, SessionId = state.SessionId,
                    StimMethodId = state.CurrentDirective?.Method?.Id ?? 1, Speed = parameters.Speed, HeldDuration = parameters.HoldDuration };
                db.Edges.Add(edge);
                await db.SaveChangesAsync(ct);
                receipt.ActivityId = edge.Id;
            }
            else
            {
                var allowed = state.Phase == SessionPhase.Finishing;
                var orgasm = new OrgasmEntry { TimestampUtc = item.OccurredAtUtc, SessionId = state.SessionId, Allowed = allowed };
                db.Orgasms.Add(orgasm);
                if (!allowed)
                {
                    var infraction = new InfractionEntry { TimestampUtc = item.OccurredAtUtc, SessionId = state.SessionId,
                        Type = InfractionType.UnauthorizedOrgasm, Notes = "Recorded by the Cove tracker." };
                    db.Infractions.Add(infraction);
                    await db.SaveChangesAsync(ct);
                    receipt.InfractionId = infraction.Id;
                }
                else await db.SaveChangesAsync(ct);
                receipt.ActivityId = orgasm.Id;
            }
            db.TrackerReceipts.Add(receipt);
            await db.SaveChangesAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_guided_event_steps ("OwnerId", "EventId", "StepNumber") VALUES ({item.UserId}, {item.Id}, {state.StepNumber})
                """, ct);
        }
        finally { runtime.Gate.Release(); }
    }

    public async Task DeliverAsync(TrackerEvent item, CancellationToken ct)
    {
        if (!runtimes.TryGet(item.UserId, out var runtime)) return;
        await runtime!.Gate.WaitAsync(ct);
        try
        {
            await using var db = await runtime.Factory.CreateDbContextAsync(ct);
            var receipt = await db.TrackerReceipts.SingleOrDefaultAsync(x => x.EventId == item.Id, ct);
            if (receipt is null || receipt.DeliveredAtUtc is not null || receipt.VoidedAtUtc is not null) return;
            if (runtime.Online.HasActiveSession && !runtime.Online.GetCurrentState().IsPaused && runtime.Online.GetCurrentState().SessionId == receipt.SessionId)
            {
                try { await ConsumeAsync(runtime, db, receipt, ct); }
                catch (Exception ex)
                {
                    // The factual event is committed. Never ask the user to record it again or
                    // replay an uncertain engine transition. Stop automation and keep the receipt.
                    logger.LogError(ex, "Guided delivery failed for tracker event {EventId}; guidance stopped", item.Id);
                    runtime.Commands.StopAfterTrackingCorrection();
                    await ClearSuspicionAsync(db, receipt.SessionId, ct);
                }
            }
            await runtime.SaveAsync(ct);
            receipt.DeliveredAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    public async Task UndoAsync(CoveContext tracker, TrackerEvent item, CancellationToken ct)
    {
        // A stopped/restarted runtime still needs its persisted activity corrected.
        var runtime = runtimes.ForUser(item.UserId);
        await runtime.Gate.WaitAsync(ct);
        try
        {
            await using var db = await runtime.Factory.InTrackerTransactionAsync(tracker, ct);
            var receipt = await db.TrackerReceipts.SingleOrDefaultAsync(x => x.EventId == item.Id, ct);
            if (receipt is null || receipt.VoidedAtUtc is not null) return;
            if (receipt.Kind == "edge")
                await db.Edges.Where(x => x.Id == receipt.ActivityId).ExecuteDeleteAsync(ct);
            else
                await db.Orgasms.Where(x => x.Id == receipt.ActivityId).ExecuteDeleteAsync(ct);
            if (receipt.InfractionId is int infraction)
                await db.Infractions.Where(x => x.Id == infraction).ExecuteDeleteAsync(ct);
            receipt.VoidedAtUtc = DateTime.UtcNow;
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == receipt.SessionId, ct);
            if (session is not null)
            {
                session.SuspectedUnauthorizedOrgasm = false;
                session.Outcome = SessionOutcome.Unknown;
            }
            await db.SaveChangesAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    public async Task CorrectRuntimeAsync(TrackerEvent item, CancellationToken ct)
    {
        if (!runtimes.TryGet(item.UserId, out var runtime)) return;
        await runtime!.Gate.WaitAsync(ct);
        try
        {
            await using var db = await runtime.Factory.CreateDbContextAsync(ct);
            var receipt = await db.TrackerReceipts.SingleOrDefaultAsync(x => x.EventId == item.Id, ct);
            if (receipt is not null && runtime.Online.HasActiveSession && runtime.Online.GetCurrentState().SessionId == receipt.SessionId)
                runtime.Commands.StopAfterTrackingCorrection();
            await runtime.SaveAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    private static async Task ClearSuspicionAsync(CommandCenterDbContext db, int sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is not null) session.SuspectedUnauthorizedOrgasm = false;
    }
    private static async Task ConsumeAsync(GuidedRuntime runtime, CommandCenterDbContext db, GuidedEventReceipt receipt, CancellationToken ct)
    {
        var originalStep = await db.Database.SqlQuery<int>($"""
            SELECT "StepNumber" AS "Value" FROM aidomme_guided_event_steps WHERE "OwnerId" = {db.OwnerId} AND "EventId" = {receipt.EventId}
            """).SingleOrDefaultAsync(ct);
        if (receipt.Kind == "edge" && originalStep > 0 && originalStep != runtime.Online.GetCurrentState().StepNumber)
        {
            runtime.Commands.ObserveEarlierTrackerEdge(receipt.EventId);
            return;
        }
        var allowed = receipt.Kind == "orgasm" ? (await db.Orgasms.SingleAsync(x => x.Id == receipt.ActivityId, ct)).Allowed : (bool?)null;
        await runtime.Commands.ConsumeTrackerEventAsync(receipt.EventId, receipt.Kind, ct, allowed);
    }
}

public sealed record GuidedActionRequest(Guid EventId, Guid SettingId, DateTime OccurredAtUtc,
    string Source = "button", string? MediaKind = null, int? MediaId = null, double? PositionSeconds = null);
