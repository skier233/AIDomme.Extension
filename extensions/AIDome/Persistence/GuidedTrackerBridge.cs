using AIDome.Data;
using AIDome.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome.Persistence;

/// <summary>
/// Connects the app-wide tracker to a running guided session. Guided records are written in the
/// tracker's own context and transaction, so an event and its guided receipt commit together.
/// The engine is told about the event after the commit, exactly once per event.
/// </summary>
public sealed class GuidedTrackerBridge(GuidedRuntimes runtimes, ILogger<GuidedTrackerBridge> logger)
{
    public async Task DrainUnderGateAsync(GuidedRuntime runtime, CancellationToken ct)
    {
        await using var db = runtime.Factory.Open();
        if (runtime.Session.ActiveSessionId is int active && await db.TrackerReceipts.AnyAsync(x => x.SessionId == active && x.VoidedAtUtc != null, ct))
            await runtime.Session.StopAfterTrackingCorrectionAsync(ct);
        var pending = await db.TrackerReceipts.Where(x => x.DeliveredAtUtc == null && x.VoidedAtUtc == null).OrderBy(x => x.Id).ToListAsync(ct);
        foreach (var receipt in pending)
        {
            if (Consuming(runtime, receipt))
            {
                try { await ConsumeAsync(runtime, db, receipt, ct); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Guided delivery failed for {EventId}; guidance stopped", receipt.EventId);
                    await runtime.Session.StopAfterTrackingCorrectionAsync(ct);
                    await ClearSuspicionAsync(db, receipt.SessionId, ct);
                }
            }
            await runtime.SaveAsync(ct);
            receipt.DeliveredAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordAsync(DbContext tracker, TrackerEvent item, CancellationToken ct, RecordTrackerEvent? expected = null)
    {
        if (!runtimes.TryGet(item.UserId, out var runtime) || !runtime!.Session.Active) return;
        await runtime.Gate.WaitAsync(ct);
        try
        {
            if (runtime.Session.ActiveSessionId is not int sessionId || runtime.Session.Paused) return;
            int step = runtime.Session.StepNumber;
            if (expected?.GuidedSessionId is int expectedSession && expectedSession != sessionId
                || expected?.GuidedStepNumber is int expectedStep && expectedStep != step) return;
            var db = runtime.Factory.Join(tracker);
            var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct)
                ?? throw new TrackerValidationException("The guided session changed. Refresh Cove before recording.");
            session.LastInteractionUtc = item.ReceivedAtUtc;
            var receipt = new GuidedEventReceipt { EventId = item.Id, Kind = item.Kind, SessionId = sessionId, StepNumber = step };
            if (item.Kind == "edge")
            {
                // The prescribed method and speed at the moment of the edge. Nothing is invented.
                var (methodId, speed) = runtime.Session.EdgeAttribution();
                var edge = db.Add(new EdgeEntry { TimestampUtc = item.OccurredAtUtc, SessionId = sessionId,
                    StimMethodId = methodId, Speed = speed, HeldDuration = TimeSpan.Zero });
                await db.SaveChangesAsync(ct);
                receipt.ActivityId = edge.Id;
            }
            else
            {
                var allowed = runtime.Session.PermissionStands;
                var orgasm = db.Add(new OrgasmEntry { TimestampUtc = item.OccurredAtUtc, SessionId = sessionId, Allowed = allowed });
                if (!allowed)
                {
                    var infraction = db.Add(new InfractionEntry { TimestampUtc = item.OccurredAtUtc, SessionId = sessionId,
                        Type = InfractionType.UnauthorizedOrgasm, Notes = "Recorded by the Cove tracker." });
                    await db.SaveChangesAsync(ct);
                    receipt.InfractionId = infraction.Id;
                }
                else await db.SaveChangesAsync(ct);
                receipt.ActivityId = orgasm.Id;
            }
            db.Add(receipt);
            await db.SaveChangesAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    public async Task DeliverAsync(TrackerEvent item, CancellationToken ct)
    {
        if (!runtimes.TryGet(item.UserId, out var runtime)) return;
        await runtime!.Gate.WaitAsync(ct);
        try
        {
            await using var db = runtime.Factory.Open();
            var receipt = await db.TrackerReceipts.SingleOrDefaultAsync(x => x.EventId == item.Id, ct);
            if (receipt is null || receipt.DeliveredAtUtc is not null || receipt.VoidedAtUtc is not null) return;
            if (Consuming(runtime, receipt))
            {
                try { await ConsumeAsync(runtime, db, receipt, ct); }
                catch (Exception ex)
                {
                    // The factual event is committed. Never ask the user to record it again or
                    // replay an uncertain engine transition. Stop automation and keep the receipt.
                    logger.LogError(ex, "Guided delivery failed for tracker event {EventId}; guidance stopped", item.Id);
                    await runtime.Session.StopAfterTrackingCorrectionAsync(ct);
                    await ClearSuspicionAsync(db, receipt.SessionId, ct);
                }
            }
            await runtime.SaveAsync(ct);
            receipt.DeliveredAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    public async Task UndoAsync(DbContext tracker, TrackerEvent item, CancellationToken ct)
    {
        // A stopped/restarted runtime still needs its persisted activity corrected.
        var runtime = await runtimes.ForUserAsync(item.UserId, ct);
        await runtime.Gate.WaitAsync(ct);
        try
        {
            var db = runtime.Factory.Join(tracker);
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
            await using var db = runtime.Factory.Open();
            var receipt = await db.TrackerReceipts.SingleOrDefaultAsync(x => x.EventId == item.Id, ct);
            if (receipt is not null && runtime.Session.ActiveSessionId == receipt.SessionId)
                await runtime.Session.StopAfterTrackingCorrectionAsync(ct);
            await runtime.SaveAsync(ct);
        }
        finally { runtime.Gate.Release(); }
    }

    private static async Task ClearSuspicionAsync(GuidedData db, int sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (session is not null) session.SuspectedUnauthorizedOrgasm = false;
    }

    private static async Task ConsumeAsync(GuidedRuntime runtime, GuidedData db, GuidedEventReceipt receipt, CancellationToken ct)
    {
        if (receipt.Kind == "edge" && receipt.StepNumber is int step && step > 0 && step != runtime.Session.StepNumber)
        {
            await runtime.Session.ObserveEarlierEdgeAsync(receipt.EventId, ct);
            return;
        }
        var allowed = receipt.Kind == "orgasm" ? (await db.Orgasms.SingleAsync(x => x.Id == receipt.ActivityId, ct)).Allowed : (bool?)null;
        await runtime.Session.ConsumeTrackerEventAsync(receipt.EventId, receipt.Kind, allowed, ct);
    }

    private static bool Consuming(GuidedRuntime runtime, GuidedEventReceipt receipt)
        => runtime.Session.ActiveSessionId == receipt.SessionId && !runtime.Session.Paused;
}

public sealed record GuidedActionRequest(Guid EventId, Guid SettingId, DateTime OccurredAtUtc,
    string Source = "button", string? MediaKind = null, int? MediaId = null, double? PositionSeconds = null);
