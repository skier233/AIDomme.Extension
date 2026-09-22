using System.Text.Json;
using AIDome.Services;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

public sealed record GuidedCheckpoint(int Format, string Engine, ChatMessage[] Messages, Guid[] ConsumedEvents,
    int PolicyRevision, Guid[] Operations);

/// <summary>
/// Saves and restores an account's live runtime (engine state, chat, consumed tracker events,
/// applied controls). A restored session always comes back paused; nothing is replayed.
/// </summary>
public static class GuidedRecovery
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Status polls refresh a running countdown at most this often.</summary>
    private static readonly TimeSpan PollSaveInterval = TimeSpan.FromSeconds(15);

    public static async Task LoadAsync(GuidedRuntime runtime, CancellationToken ct)
    {
        await using var db = runtime.Factory.Open();
        var saved = await db.Database.SqlQuery<RecoveryRow>($"""
            SELECT "Revision", "Payload" FROM aidomme_runtime_checkpoints WHERE "UserId" = {db.UserId}
            """).SingleOrDefaultAsync(ct);
        if (saved is null) return;
        var checkpoint = JsonSerializer.Deserialize<GuidedCheckpoint>(saved.Payload, Json)
            ?? throw new InvalidOperationException("Invalid saved companion state.");
        if (checkpoint.Format != 1) throw new InvalidOperationException("Unsupported saved companion version.");
        await runtime.Online.RestoreRecoveryAsync(checkpoint.Engine, ct);
        runtime.Commands.RestoreCompanionMessages(checkpoint.Messages, checkpoint.ConsumedEvents);
        runtime.Revision = saved.Revision;
        runtime.PolicyRevision = checkpoint.PolicyRevision;
        foreach (var operation in checkpoint.Operations) runtime.Operations.Add(operation);
        var pending = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE aidomme_session_controls SET "Status" = 'Uncertain' WHERE "UserId" = {db.UserId} AND "Status" = 'Pending'
            """, ct);
        if (pending > 0)
        {
            runtime.Online.EndSession();
            runtime.Commands.CompanionNotice("Guidance stopped because a command was interrupted. Its effects were not replayed. Review the recorded activity before starting another session.");
        }
        if (runtime.Online.HasActiveSession) runtime.Commands.CompanionNotice("Session recovered paused. Review the current step, then resume when ready. No missed timers or narration were replayed.");
        runtime.LastSavedPayload = null;
    }

    public static async Task SaveAsync(GuidedRuntime runtime, bool force, CancellationToken ct)
    {
        var checkpoint = new GuidedCheckpoint(1, runtime.Online.ExportRecovery(), runtime.Commands.GetLatestMessages().ToArray(),
            runtime.Commands.ConsumedTrackerEvents(), runtime.PolicyRevision, runtime.Operations.ToArray());
        var payload = JsonSerializer.Serialize(checkpoint, Json);
        var now = DateTime.UtcNow;
        if (payload == runtime.LastSavedPayload) return;
        if (!force && now - runtime.LastSavedAtUtc < PollSaveInterval) return;

        var revision = runtime.Revision + 1;
        var state = runtime.Online.GetCurrentState();
        var policy = state.SessionId > 0 && runtime.PolicyRevision > 0 ? JsonSerializer.Serialize(runtime.Online.Policy, Json) : null;
        var operations = runtime.Operations.ToArray();
        await using var db = runtime.Factory.Open();
        await db.InTransactionAsync(async () =>
        {
            if (policy is not null)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO aidomme_session_policies ("UserId", "SessionId", "Revision", "Policy", "AcceptedAtUtc")
                    VALUES ({db.UserId}, {state.SessionId}, {runtime.PolicyRevision}, {policy}, {now}) ON CONFLICT DO NOTHING
                    """, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_runtime_checkpoints ("UserId", "Revision", "Payload", "UpdatedAtUtc")
                VALUES ({db.UserId}, {revision}, {payload}, {now})
                ON CONFLICT ("UserId") DO UPDATE SET "Revision" = EXCLUDED."Revision", "Payload" = EXCLUDED."Payload", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
                """, ct);
            if (operations.Length > 0)
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE aidomme_session_controls SET "Status" = 'Applied' WHERE "UserId" = {db.UserId} AND "EventId" = ANY({operations})
                    """, ct);
            return true;
        }, ct);
        runtime.Revision = revision;
        runtime.LastSavedPayload = payload;
        runtime.LastSavedAtUtc = now;
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
