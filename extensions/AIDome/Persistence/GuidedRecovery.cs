using System.Text.Json;
using AIDome.Sessions;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Persistence;

/// <summary>
/// Format 2: the session engine's own state plus the chat log, consumed tracker events and applied
/// controls. Format 1 (before the session contract) restores its chat log only.
/// </summary>
public sealed record GuidedCheckpoint(int Format, string? Session, ChatMessage[] Messages, Guid[] ConsumedEvents, Guid[] Operations,
    DateTime SavedAtUtc);

/// <summary>
/// Saves and restores an account's live runtime. A restored session always comes back paused with
/// its countdown as of the last save; nothing is replayed.
/// </summary>
public static class GuidedRecovery
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
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
            SELECT "Revision", "Payload", "UpdatedAtUtc" FROM aidomme_runtime_checkpoints WHERE "UserId" = {db.UserId}
            """).SingleOrDefaultAsync(ct);
        if (saved is null) return;
        using var document = JsonDocument.Parse(saved.Payload);
        int format = document.RootElement.TryGetProperty("format", out var f) ? f.GetInt32() : 0;
        var messages = document.RootElement.TryGetProperty("messages", out var m) ? m.Deserialize<ChatMessage[]>(Json) ?? [] : [];
        var consumed = document.RootElement.TryGetProperty("consumedEvents", out var c) ? c.Deserialize<Guid[]>(Json) ?? [] : [];
        runtime.Revision = saved.Revision;
        if (format == 1)
        {
            // Saved by the engine before session contracts; its sessions cannot continue on this one.
            bool wasActive = document.RootElement.TryGetProperty("engine", out var engine)
                && JsonDocument.Parse(engine.GetString() ?? "{}").RootElement is var e
                && e.TryGetProperty("sessionId", out var id) && id.GetInt32() > 0
                && !(e.TryGetProperty("ended", out var ended) && ended.GetBoolean());
            runtime.Session.RestoreMessages(messages, consumed);
            if (wasActive) runtime.Session.Notice("A session from an earlier version of AI Domme could not be continued after the update. Start a new one when you're ready.");
            return;
        }
        if (format != 2) throw new InvalidOperationException("Unsupported saved session version.");
        var checkpoint = JsonSerializer.Deserialize<GuidedCheckpoint>(saved.Payload, Json)
            ?? throw new InvalidOperationException("Invalid saved session state.");
        runtime.Session.Restore(checkpoint.Session, checkpoint.SavedAtUtc, checkpoint.Messages, checkpoint.ConsumedEvents);
        foreach (var operation in checkpoint.Operations) runtime.Operations.Add(operation);
        var pending = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE aidomme_session_controls SET "Status" = 'Uncertain' WHERE "UserId" = {db.UserId} AND "Status" = 'Pending'
            """, ct);
        if (pending > 0)
            runtime.Session.Abandon("Guidance stopped because a command was interrupted. Its effects were not replayed. Review the recorded activity before starting another session.");
        else if (runtime.Session.State is { Active: true, InterruptedAtUtc: not null })
            runtime.Session.Notice("Session recovered paused after Cove stopped. She'll ask what happened before you continue. No missed timers or narration were replayed.");
        else if (runtime.Session.Active)
            runtime.Session.Notice("Session recovered paused. Review the current step, then resume when you're ready. No missed timers or narration were replayed.");
    }

    public static async Task SaveAsync(GuidedRuntime runtime, bool force, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var session = runtime.Session.ExportState();
        var operations = runtime.Operations.ToArray();
        var content = JsonSerializer.Serialize(new GuidedCheckpoint(2, session, runtime.Session.Messages().ToArray(),
            runtime.Session.ConsumedEvents(), operations, default), Json);
        // State changes are saved as they happen. Status polls only refresh a running countdown, so a
        // crash restores it at most PollSaveInterval out of date.
        bool running = runtime.Session.Active && !runtime.Session.Paused;
        if (force ? content == runtime.LastSavedPayload : !running || now - runtime.LastSavedAtUtc < PollSaveInterval) return;
        var payload = JsonSerializer.Serialize(new GuidedCheckpoint(2, session, runtime.Session.Messages().ToArray(),
            runtime.Session.ConsumedEvents(), operations, now), Json);
        var revision = runtime.Revision + 1;
        await using var db = runtime.Factory.Open();
        await db.InTransactionAsync(async () =>
        {
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
        runtime.LastSavedPayload = content;
        runtime.LastSavedAtUtc = now;
    }

    public sealed class RecoveryRow
    {
        public long Revision { get; set; }
        public string Payload { get; set; } = "";
        public DateTime UpdatedAtUtc { get; set; }
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

public sealed record SessionControlRequest(Guid EventId, int SessionId, int StepNumber, string Action, int? Answer = null);
public sealed record SessionContractRequest(int SessionId, int Revision, SessionContract Contract);
public sealed record NarrationClaimRequest(Guid ClientId, Guid MessageId);
