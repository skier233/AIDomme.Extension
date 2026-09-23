using AIDome.Data;
using AIDome.Tracking;
using Cove.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AIDome.Persistence;

/// <summary>
/// Maps AI Domme's guided-session entities onto Cove's own context (via
/// <c>IDataExtension.ConfigureModel</c>). The tables themselves are created by the literal SQL
/// migrations in <see cref="GuidedMigrations"/>; this mapping must stay in step with them.
/// Account ownership is an explicit <c>UserId</c> column: every read goes through
/// <see cref="GuidedData"/>, which filters by the signed-in account.
/// </summary>
public static class GuidedSchema
{
    public static void Configure(ModelBuilder model)
    {
        Definition<StimMethod>(model, "aidomme_methods", e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
        });
        Definition<PunishmentEnder>(model, "aidomme_enders", e =>
        {
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.InstructionText).HasMaxLength(2048);
            e.Property(x => x.AnnouncementText).HasMaxLength(512);
        });
        model.Entity<TuningRatingEntry>(e =>
        {
            e.ToTable("aidomme_tuning_ratings");
            e.HasKey(x => x.Id);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.CandidateKey).HasMaxLength(256);
            e.Property(x => x.Mode).HasMaxLength(32);
            e.Property(x => x.Group).HasMaxLength(64);
            e.Property(x => x.Title).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(1024);
        });
        model.Entity<TuningComparisonEntry>(e =>
        {
            e.ToTable("aidomme_tuning_comparisons");
            e.HasKey(x => x.Id);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Mode).HasMaxLength(32);
            e.Property(x => x.WinnerKey).HasMaxLength(256);
            e.Property(x => x.LoserKey).HasMaxLength(256);
        });

        Owned<SessionRecord>(model, "aidomme_sessions", e =>
        {
            e.HasMany(x => x.Edges).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Orgasms).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Instructions).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Punishments).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Games).WithOne(x => x.Session).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Mode).HasMaxLength(32);
        });
        Owned<EdgeEntry>(model, "aidomme_edges", e =>
        {
            e.HasOne(x => x.StimMethod).WithMany(x => x.Edges).HasForeignKey(x => x.StimMethodId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.HeldDuration).HasConversion(v => v.TotalSeconds, v => TimeSpan.FromSeconds(v));
        });
        Owned<OrgasmEntry>(model, "aidomme_orgasms");
        Owned<CumRequest>(model, "aidomme_permission_requests", e =>
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade));
        Owned<InstructionStep>(model, "aidomme_steps", e =>
        {
            e.HasOne(x => x.StimMethod).WithMany().HasForeignKey(x => x.StimMethodId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Text).HasMaxLength(1024);
        });
        Owned<PunishmentEntry>(model, "aidomme_punishments", e => e.Property(x => x.Description).HasMaxLength(1024));
        Owned<InfractionEntry>(model, "aidomme_infractions", e =>
        {
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Notes).HasMaxLength(512);
        });
        Owned<GameChallenge>(model, "aidomme_games", e =>
        {
            e.Property(x => x.Prompt).HasMaxLength(1024);
            e.Property(x => x.FailureConsequence).HasMaxLength(512);
            e.Property(x => x.SuccessReward).HasMaxLength(512);
        });
        Owned<GuidedEventReceipt>(model, "aidomme_tracker_receipts", e =>
        {
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TrackerEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.EventId).IsUnique();
            e.Property(x => x.Kind).HasMaxLength(16);
        });

        Owned<OfferRecord>(model, "aidomme_offers", e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<SessionRecord>().WithMany().HasForeignKey(x => x.OriginSessionId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Terms).HasMaxLength(2000);
            e.Property(x => x.Unit).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(16);
        });
        Owned<CommitmentRecord>(model, "aidomme_commitments", e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<OfferRecord>().WithMany().HasForeignKey(x => x.OfferId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Terms).HasMaxLength(2000);
            e.Property(x => x.Unit).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(16);
        });
        Owned<CommitmentReportRecord>(model, "aidomme_commitment_reports", e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasOne<CommitmentRecord>().WithMany().HasForeignKey(x => x.CommitmentId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(1000);
        });
    }

    /// <summary>The entity types this extension adds to Cove's model.</summary>
    public static readonly Type[] EntityTypes =
    [
        typeof(StimMethod), typeof(PunishmentEnder), typeof(TuningRatingEntry), typeof(TuningComparisonEntry),
        typeof(SessionRecord), typeof(EdgeEntry), typeof(OrgasmEntry), typeof(CumRequest), typeof(InstructionStep),
        typeof(PunishmentEntry), typeof(InfractionEntry), typeof(GameChallenge), typeof(GuidedEventReceipt),
        typeof(OfferRecord), typeof(CommitmentRecord), typeof(CommitmentReportRecord),
    ];

    private static void Owned<T>(ModelBuilder model, string table, Action<EntityTypeBuilder<T>>? configure = null)
        where T : class, IUserOwned
    {
        var e = model.Entity<T>();
        e.ToTable(table);
        e.HasKey("Id");
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        configure?.Invoke(e);
    }

    private static void Definition<T>(ModelBuilder model, string table, Action<EntityTypeBuilder<T>> configure)
        where T : class, IDefinition<T>
    {
        var e = model.Entity<T>();
        e.ToTable(table);
        e.HasKey(x => x.Id);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<T>().WithMany().HasForeignKey(x => x.BaseId).OnDelete(DeleteBehavior.Cascade);
        configure(e);
    }
}

/// <summary>Literal, forward-only SQL migrations, applied by Cove and recorded by name.</summary>
public static class GuidedMigrations
{
    /// <summary>002-004 created an earlier account-composite-key schema. 005 moves any such data and
    /// replaces it, so fresh installs skip straight to 005. The names stay so receipts line up.</summary>
    public const string Superseded = "SELECT 1; -- superseded by 005_guided_v2";

    public static string Load(string name)
    {
        using var stream = typeof(GuidedMigrations).Assembly.GetManifestResourceStream($"AIDome.Sql.{name}.sql")
            ?? throw new InvalidOperationException($"Migration {name} is not embedded in the extension.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
