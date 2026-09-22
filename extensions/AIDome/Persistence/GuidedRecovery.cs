using System.Text.Json;
using AIDome.Services;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record GuidedCheckpoint(int Format, string Engine, ChatMessage[] Messages, Guid[] ConsumedEvents,
    int PolicyRevision, Guid[] Operations);

public static class GuidedRecovery
{
    public const string Sql = """
        CREATE TABLE aidomme_guided_recovery (
          "OwnerId" integer PRIMARY KEY REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
          "Revision" bigint NOT NULL, "Payload" text NOT NULL, "UpdatedAtUtc" timestamptz NOT NULL,
          CHECK ("OwnerId" > 0));
        CREATE TABLE aidomme_narration_receipts (
          "OwnerId" integer NOT NULL REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
          "MessageId" uuid NOT NULL, "ClaimedAtUtc" timestamptz NOT NULL,
          PRIMARY KEY ("OwnerId", "MessageId"));
        CREATE TABLE aidomme_guided_policies (
          "OwnerId" integer NOT NULL, "SessionId" integer NOT NULL, "Revision" integer NOT NULL,
          "Policy" text NOT NULL, "AcceptedAtUtc" timestamptz NOT NULL,
          PRIMARY KEY ("OwnerId", "SessionId", "Revision"),
          FOREIGN KEY ("OwnerId", "SessionId") REFERENCES aidomme_guided_sessionrecord("OwnerId", "Id") ON DELETE CASCADE);
        CREATE TABLE aidomme_guided_controls (
          "OwnerId" integer NOT NULL REFERENCES aidomme_guided_accounts("OwnerId") ON DELETE CASCADE,
          "EventId" uuid NOT NULL, "Payload" text NOT NULL, "Status" text NOT NULL,
          PRIMARY KEY ("OwnerId", "EventId"), CHECK ("Status" IN ('Pending', 'Applied', 'Uncertain')));
        CREATE TABLE aidomme_guided_event_steps (
          "OwnerId" integer NOT NULL, "EventId" uuid NOT NULL, "StepNumber" integer NOT NULL,
          PRIMARY KEY ("OwnerId", "EventId"),
          FOREIGN KEY ("OwnerId", "EventId") REFERENCES aidomme_guided_guidedeventreceipt("OwnerId", "EventId") ON DELETE CASCADE);
        """;

    public static async Task LoadAsync(GuidedRuntime runtime, CancellationToken ct)
    {
        await using var db = await runtime.Factory.CreateDbContextAsync(ct);
        var saved = await db.Database.SqlQuery<RecoveryRow>($"""
            SELECT "Revision", "Payload" FROM aidomme_guided_recovery WHERE "OwnerId" = {db.OwnerId}
            """).SingleOrDefaultAsync(ct);
        if (saved is null) return;
        var checkpoint = JsonSerializer.Deserialize<GuidedCheckpoint>(saved.Payload, GuidedStore.Json)
            ?? throw new InvalidOperationException("Invalid saved companion state.");
        if (checkpoint.Format != 1) throw new InvalidOperationException("Unsupported saved companion version.");
        await runtime.Online.RestoreRecoveryAsync(checkpoint.Engine, ct);
        runtime.Commands.RestoreCompanionMessages(checkpoint.Messages, checkpoint.ConsumedEvents);
        runtime.Revision = saved.Revision;
        runtime.PolicyRevision = checkpoint.PolicyRevision;
        foreach (var operation in checkpoint.Operations) runtime.Operations.Add(operation);
        var pending = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE aidomme_guided_controls SET "Status" = 'Uncertain' WHERE "OwnerId" = {db.OwnerId} AND "Status" = 'Pending'
            """, ct);
        if (pending > 0)
        {
            runtime.Online.EndSession();
            runtime.Commands.CompanionNotice("Guidance stopped because a command was interrupted. Its effects were not replayed. Review the recorded activity before starting another session.");
        }
        if (runtime.Online.HasActiveSession) runtime.Commands.CompanionNotice("Session recovered paused. Review the current step, then resume when ready. No missed timers or narration were replayed.");
    }
    public static async Task SaveAsync(GuidedRuntime runtime, CancellationToken ct)
    {
        await using var db = await runtime.Factory.CreateDbContextAsync(ct);
        var checkpoint = new GuidedCheckpoint(1, runtime.Online.ExportRecovery(), runtime.Commands.GetLatestMessages().ToArray(),
            runtime.Commands.ConsumedTrackerEvents(), runtime.PolicyRevision, runtime.Operations.ToArray());
        var payload = JsonSerializer.Serialize(checkpoint, GuidedStore.Json);
        var revision = ++runtime.Revision;
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var state = runtime.Online.GetCurrentState();
        if (state.SessionId > 0 && runtime.PolicyRevision > 0)
        {
            var policy = JsonSerializer.Serialize(runtime.Online.Policy, GuidedStore.Json);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_guided_policies ("OwnerId", "SessionId", "Revision", "Policy", "AcceptedAtUtc")
                VALUES ({db.OwnerId}, {state.SessionId}, {runtime.PolicyRevision}, {policy}, {now}) ON CONFLICT DO NOTHING
                """, ct);
        }
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO aidomme_guided_recovery ("OwnerId", "Revision", "Payload", "UpdatedAtUtc")
            VALUES ({db.OwnerId}, {revision}, {payload}, {now})
            ON CONFLICT ("OwnerId") DO UPDATE SET "Revision" = EXCLUDED."Revision", "Payload" = EXCLUDED."Payload", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """, ct);
        var operations = runtime.Operations.ToArray();
        if (operations.Length > 0)
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE aidomme_guided_controls SET "Status" = 'Applied' WHERE "OwnerId" = {db.OwnerId} AND "EventId" = ANY({operations})
                """, ct);
        await transaction.CommitAsync(ct);
    }
    public sealed class RecoveryRow
    {
        public long Revision { get; set; }
        public string Payload { get; set; } = "";
    }
    public sealed class ControlRow
    {
        public string Payload { get; set; } = "";
        public string Status { get; set; } = "";
    }
}

public class NarrationLeases
{
    private readonly AIDome.Tracking.MetronomeLeases leases = new();
    public bool Acquire(int userId, Guid client) => leases.Acquire(userId, client);
    public void Release(int userId, Guid client) => leases.Release(userId, client);
}
public sealed class MediaLeases : NarrationLeases { }

public sealed record SessionControlRequest(Guid EventId, int SessionId, int StepNumber, string Action, int? Answer = null);
public sealed record SessionPolicyRequest(int SessionId, int Revision, SessionPolicy Policy);
public sealed record NarrationClaimRequest(Guid ClientId, Guid MessageId);
