using Microsoft.EntityFrameworkCore;
using Cove.Core.Entities.Auth;

namespace AIDomme.Tracking;

public sealed class TrackerEvent
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid SettingId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public string Source { get; set; } = "";
    public string? MediaKind { get; set; }
    public int? MediaId { get; set; }
    public double? PositionSeconds { get; set; }
    public int LikeWeight { get; set; }
    // Primary keys of the exact Cove LikeCount history rows this event created.
    public string LikeIdsJson { get; set; } = "[]";
    public DateTime? VoidedAtUtc { get; set; }
}

public sealed class TrackerPreferences
{
    public int UserId { get; set; }
    public bool AutoLike { get; set; }
    public int EdgeWeight { get; set; }
    public int OrgasmWeight { get; set; } = 1;
    // Free-tracking session/bout identity, not a settings revision or a guided-session ID.
    public Guid SettingId { get; set; } = Guid.NewGuid();
}

public sealed record RecordTrackerEvent(Guid Id, Guid SettingId, string Kind, DateTime OccurredAtUtc,
    string Source, string? MediaKind = null, int? MediaId = null, double? PositionSeconds = null,
    int? GuidedSessionId = null, int? GuidedStepNumber = null);
public sealed record TrackerSettingsRequest(bool AutoLike, int EdgeWeight, int OrgasmWeight);
public sealed record TrackerSnapshot(TrackerPreferences Preferences, int Edges, int Orgasms,
    int EdgesSinceLastOrgasm, DateTime? LastOrgasmAtUtc, IReadOnlyList<TrackerEvent> Recent);
public sealed record TrackerMutation(TrackerEvent Event, TrackerSnapshot Snapshot, int? MediaLikeCount);

public static class TrackerSchema
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<TrackerEvent>(e =>
        {
            e.ToTable("aidomme_tracker_events");
            e.HasKey(x => x.Id);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.MediaKind).HasMaxLength(16);
            e.HasIndex(x => new { x.UserId, x.OccurredAtUtc });
        });
        model.Entity<TrackerPreferences>(e =>
        {
            e.ToTable("aidomme_tracker_preferences");
            e.HasKey(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.UserId).ValueGeneratedNever();
        });
    }

    public const string Sql = """
        CREATE TABLE IF NOT EXISTS aidomme_tracker_preferences (
          "UserId" integer PRIMARY KEY REFERENCES users("Id") ON DELETE CASCADE, "AutoLike" boolean NOT NULL,
          "EdgeWeight" integer NOT NULL CHECK ("EdgeWeight" BETWEEN 0 AND 1),
          "OrgasmWeight" integer NOT NULL CHECK ("OrgasmWeight" BETWEEN 0 AND 3),
          "SettingId" uuid NOT NULL);
        CREATE TABLE IF NOT EXISTS aidomme_tracker_events (
          "Id" uuid PRIMARY KEY, "UserId" integer NOT NULL REFERENCES users("Id") ON DELETE CASCADE, "SettingId" uuid NOT NULL,
          "Kind" varchar(16) NOT NULL CHECK ("Kind" IN ('edge','orgasm')),
          "OccurredAtUtc" timestamptz NOT NULL, "ReceivedAtUtc" timestamptz NOT NULL,
          "Source" varchar(16) NOT NULL, "MediaKind" varchar(16), "MediaId" integer,
          "PositionSeconds" double precision, "LikeWeight" integer NOT NULL,
          "LikeIdsJson" text NOT NULL, "VoidedAtUtc" timestamptz);
        CREATE INDEX IF NOT EXISTS "IX_aidomme_tracker_events_UserId_OccurredAtUtc"
          ON aidomme_tracker_events ("UserId", "OccurredAtUtc");
        """;
}
