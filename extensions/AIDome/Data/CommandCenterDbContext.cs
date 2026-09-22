using System;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Data;

public sealed class CommandCenterDbContext(DbContextOptions<CommandCenterDbContext> options, int ownerId = 0) : DbContext(options)
{
    public int OwnerId { get; } = ownerId;
    public bool ApplyingDefaults { get; set; }
    public DbSet<GuidedEventReceipt> TrackerReceipts => Set<GuidedEventReceipt>();
    public DbSet<StimMethod> StimMethods => Set<StimMethod>();
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();
    public DbSet<EdgeEntry> Edges => Set<EdgeEntry>();
    public DbSet<OrgasmEntry> Orgasms => Set<OrgasmEntry>();
    public DbSet<CumRequest> CumRequests => Set<CumRequest>();
    public DbSet<InstructionStep> InstructionSteps => Set<InstructionStep>();
    public DbSet<PunishmentEntry> Punishments => Set<PunishmentEntry>();
    public DbSet<InfractionEntry> Infractions => Set<InfractionEntry>();
    public DbSet<GameChallenge> GameChallenges => Set<GameChallenge>();
    public DbSet<PunishmentEnder> PunishmentEnders => Set<PunishmentEnder>();
    public DbSet<TuningRatingEntry> TuningRatings => Set<TuningRatingEntry>();
    public DbSet<TuningComparisonEntry> TuningComparisonHistory => Set<TuningComparisonEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (!Database.IsNpgsql())
            foreach (var type in AIDome.Persistence.GuidedSchema.DefinitionTypes) modelBuilder.Entity(type).Ignore("OwnerId");
        // ── StimMethod ──
        var stim = modelBuilder.Entity<StimMethod>();
        stim.HasKey(x => x.Id);
        stim.Property(x => x.Name).HasMaxLength(128).IsRequired();
        stim.Property(x => x.Description).HasMaxLength(512).HasDefaultValue(string.Empty);
        stim.Property(x => x.TortLevel).HasDefaultValue(0);
        stim.Property(x => x.Intensity).HasDefaultValue(1);
        stim.Property(x => x.Tease).HasDefaultValue(0);
        stim.Property(x => x.Closeness).HasDefaultValue(0);
        stim.Property(x => x.RequiresLube).HasDefaultValue(false);
        stim.Property(x => x.ToyCategory).HasDefaultValue(ToyCategory.None);
        stim.Property(x => x.TimePointMultiplier).HasDefaultValue(0d);
        stim.Property(x => x.EdgePointMultiplier).HasDefaultValue(0d);
        SeedStimMethods(stim);

        // ── SessionRecord ──
        var session = modelBuilder.Entity<SessionRecord>();
        session.HasKey(x => x.Id);
        session.Property(x => x.StartedAtUtc).IsRequired();
        session.Property(x => x.LastInteractionUtc).IsRequired();
        session.Property(x => x.Intensity).HasDefaultValue(-1);
        session.Property(x => x.Length).HasDefaultValue(-1);
        session.Property(x => x.Denial).HasDefaultValue(-1);
        session.Property(x => x.Kink).HasDefaultValue(-1);
        session.Property(x => x.Mean).HasDefaultValue(-1);
        session.Property(x => x.AllowToys).HasDefaultValue(false);
        session.Property(x => x.AllowVibrator).HasDefaultValue(false);
        session.Property(x => x.AllowFleshlight).HasDefaultValue(false);
        session.Property(x => x.AllowAutoblow).HasDefaultValue(false);
        session.Property(x => x.AllowProstate).HasDefaultValue(false);
        session.Property(x => x.AllowAnal).HasDefaultValue(false);
        session.Property(x => x.AllowBondage).HasDefaultValue(false);
        session.Property(x => x.AllowCEI).HasDefaultValue(false);
        session.Property(x => x.IsDebugSession).HasDefaultValue(false);
        session.Property(x => x.SuspectedUnauthorizedOrgasm).HasDefaultValue(false);
        session.Property(x => x.Outcome).HasDefaultValue(SessionOutcome.Unknown);

        // ── EdgeEntry ──
        var edge = modelBuilder.Entity<EdgeEntry>();
        edge.HasKey(x => x.Id);
        edge.HasIndex(x => x.TimestampUtc);
        edge.Property(x => x.Speed).HasDefaultValue(0);
        edge.Property(x => x.HeldDuration).HasConversion(
            v => v.TotalSeconds,
            v => TimeSpan.FromSeconds(v));
        edge.HasOne(x => x.StimMethod)
            .WithMany(x => x.Edges)
            .HasForeignKey(x => x.StimMethodId)
            .OnDelete(DeleteBehavior.Restrict);
        edge.HasOne(x => x.Session)
            .WithMany(x => x.Edges)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── OrgasmEntry ──
        var orgasm = modelBuilder.Entity<OrgasmEntry>();
        orgasm.HasKey(x => x.Id);
        orgasm.HasIndex(x => x.TimestampUtc);
        orgasm.HasOne(x => x.Session)
            .WithMany(x => x.Orgasms)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── CumRequest ──
        var cum = modelBuilder.Entity<CumRequest>();
        cum.HasKey(x => x.Id);
        cum.HasIndex(x => x.TimestampUtc);
        cum.Property(x => x.Decision).IsRequired();
        cum.HasOne(x => x.Session)
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── InstructionStep ──
        var step = modelBuilder.Entity<InstructionStep>();
        step.HasKey(x => x.Id);
        step.HasIndex(x => new { x.SessionId, x.Ordinal });
        step.Property(x => x.Text).HasMaxLength(1024).HasDefaultValue(string.Empty);
        step.Property(x => x.Kind).HasDefaultValue(InstructionKind.TimedMethod);
        step.Property(x => x.HoldSeconds).HasDefaultValue(0);
        step.HasOne(x => x.Session)
            .WithMany(x => x.Instructions)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        step.HasOne(x => x.StimMethod)
            .WithMany()
            .HasForeignKey(x => x.StimMethodId)
            .OnDelete(DeleteBehavior.SetNull);

        // ── PunishmentEntry ──
        var pun = modelBuilder.Entity<PunishmentEntry>();
        pun.HasKey(x => x.Id);
        pun.HasIndex(x => x.IssuedAtUtc);
        pun.Property(x => x.Description).HasMaxLength(1024).HasDefaultValue(string.Empty);
        pun.HasOne(x => x.Session)
            .WithMany(x => x.Punishments)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── InfractionEntry ──
        var infraction = modelBuilder.Entity<InfractionEntry>();
        infraction.HasKey(x => x.Id);
        infraction.HasIndex(x => x.TimestampUtc);
        infraction.Property(x => x.Notes).HasMaxLength(512).HasDefaultValue(string.Empty);
        infraction.HasOne(x => x.Session)
            .WithMany()
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── GameChallenge ──
        var game = modelBuilder.Entity<GameChallenge>();
        game.HasKey(x => x.Id);
        game.HasIndex(x => x.IssuedAtUtc);
        game.Property(x => x.Prompt).HasMaxLength(1024).HasDefaultValue(string.Empty);
        game.Property(x => x.FailureConsequence).HasMaxLength(512).HasDefaultValue(string.Empty);
        game.Property(x => x.SuccessReward).HasMaxLength(512).HasDefaultValue(string.Empty);
        game.HasOne(x => x.Session)
            .WithMany(x => x.Games)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── PunishmentEnder ──
        var ender = modelBuilder.Entity<PunishmentEnder>();
        ender.HasKey(x => x.Id);
        ender.Property(x => x.Name).HasMaxLength(128).IsRequired();
        ender.Property(x => x.InstructionText).HasMaxLength(2048).HasDefaultValue(string.Empty);
        ender.Property(x => x.AnnouncementText).HasMaxLength(512).HasDefaultValue(string.Empty);
        ender.Property(x => x.Severity).HasDefaultValue(1);
        ender.Property(x => x.Anticipation).HasDefaultValue(1);
        ender.Property(x => x.Humiliation).HasDefaultValue(1);
        SeedPunishmentEnders(ender);

        // ── TuningRatingEntry ──
        var tuning = modelBuilder.Entity<TuningRatingEntry>();
        tuning.HasKey(x => x.Id);
        tuning.HasIndex(x => x.CandidateKey).IsUnique();
        tuning.Property(x => x.CandidateKey).HasMaxLength(256).IsRequired();
        tuning.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        tuning.Property(x => x.Group).HasMaxLength(64).HasDefaultValue(string.Empty);
        tuning.Property(x => x.Title).HasMaxLength(128).HasDefaultValue(string.Empty);
        tuning.Property(x => x.Description).HasMaxLength(1024).HasDefaultValue(string.Empty);
        tuning.Property(x => x.SeedRating).HasDefaultValue(1000d);
        tuning.Property(x => x.Rating).HasDefaultValue(1000d);
        tuning.Property(x => x.Wins).HasDefaultValue(0);
        tuning.Property(x => x.Losses).HasDefaultValue(0);
        tuning.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");

        // ── TuningComparisonEntry ──
        var comparison = modelBuilder.Entity<TuningComparisonEntry>();
        comparison.HasKey(x => x.Id);
        comparison.Property(x => x.Mode).HasMaxLength(32).IsRequired();
        comparison.Property(x => x.WinnerKey).HasMaxLength(256).IsRequired();
        comparison.Property(x => x.LoserKey).HasMaxLength(256).IsRequired();
        comparison.Property(x => x.TimestampUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        comparison.HasIndex(x => new { x.Mode, x.WinnerKey });
        comparison.HasIndex(x => new { x.Mode, x.LoserKey });

        // ── SessionRecord.Points ──
        session.Property(x => x.Points).HasDefaultValue(0);
        if (Database.IsNpgsql()) AIDome.Persistence.GuidedSchema.ConfigureOwned(modelBuilder, this);
        else modelBuilder.Ignore<GuidedEventReceipt>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareOwnership();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareOwnership();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareOwnership()
    {
        if (!Database.IsNpgsql()) return;
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            if (entry.State == EntityState.Added) entry.Property("OwnerId").CurrentValue = OwnerId;
            if ((int)entry.Property("OwnerId").CurrentValue! != OwnerId)
                throw new InvalidOperationException("Cannot modify another account's guided data.");
            if (OwnerId == 0 && !AIDome.Persistence.GuidedSchema.IsDefinition(entry.Metadata.ClrType))
                throw new InvalidOperationException("Shared defaults cannot contain activity.");
            if (!ApplyingDefaults && AIDome.Persistence.GuidedSchema.IsDefinition(entry.Metadata.ClrType)
                && entry.State is EntityState.Added or EntityState.Modified)
                entry.Property("IsOverride").CurrentValue = OwnerId != 0;
        }
    }

    /// <summary>Seeds all 40 stim methods (30 manual + 10 toy-based) from the methods reference data.</summary>
    private static void SeedStimMethods(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<StimMethod> stim)
    {
        // ── Manual methods (hands only) ──
        stim.HasData(
            M(1, "Figure Eight", 6, 0, 4, 7, true, "Perform the figure eight on your cock."),
            M(2, "Stroke From Under (dry)", 4, 0, 1, 3, false, "Stroke your cock from under your leg."),
            M(3, "Stroke From Under (lube)", 6, 0, 3, 6, true, "Stroke your cock from under your leg."),
            M(4, "Palm", 9, 0, 9, 1, true, "Palm your cock."),
            M(5, "Worse Than Palm", 10, 0, 10, 2, true, "Perform worse than palming on your cock."),
            M(6, "Top-Bottom Grip", 5, 0, 2, 4, true, "Place one hand palm down on the top of your cock and the other lengthwise on the bottom and wrap the fingers of the top hand around. Then stroke."),
            M(7, "Roll Between Hands", 3, 3, 0, 3, false, "Roll your cock between your hands."),
            M(8, "Stomach Rub", 4, 7, 0, 3, true, "Rub your cock back and forth across your stomach."),
            M(9, "Stomach Palm", 5, 6, 0, 4, true, "Push your cock against your stomach and then move your palm up and down on top of it."),
            M(10, "1-Hand Stroke (dry)", 4, 0, 1, 3, false, "Stroke up and down your cock with 1 hand."),
            M(11, "1-Hand Stroke (lube)", 6, 0, 3, 5, true, "Stroke up and down your cock with 1 hand."),
            M(12, "2-Hand Stroke", 7, 0, 2, 8, true, "Stroke up and down your cock with 2 hands."),
            M(13, "Head Stroke", 6, 2, 4, 6, true, "Stroke up and down the head of your cock."),
            M(14, "Tip Stroke", 7, 4, 6, 3, true, "Stroke up and down the tip of your cock."),
            M(15, "Skin-Held Shaft Stroke", 8, 0, 3, 9, true, "Hold down the skin of your cock and stroke the shaft with the other hand."),
            M(16, "Skin-Held Head Stroke", 7, 2, 5, 7, true, "Hold down the skin of your cock and stroke the head with the other hand."),
            M(17, "Skin-Held Tip Stroke", 8, 4, 7, 4, true, "Hold down the skin of your cock and stroke the tip with the other hand."),
            M(18, "Sweet Spot Rub", 3, 3, 0, 7, false, "Rub/tickle the sweet spot of your cock."),
            M(19, "Twist Opposite", 7, 0, 2, 8, true, "Twist in opposite directions on your cock."),
            M(20, "Twist Head", 6, 2, 4, 6, true, "Twist the head of your cock."),
            M(21, "Twist Tip", 7, 4, 6, 3, true, "Twist the tip of your cock."),
            M(22, "Skin-Held Shaft Twist", 8, 0, 3, 9, true, "Hold down the skin of your cock and twist the shaft with the other hand."),
            M(23, "Skin-Held Head Twist", 7, 2, 5, 7, true, "Hold down the skin of your cock and twist the head with the other hand."),
            M(24, "Skin-Held Tip Twist", 8, 4, 7, 4, true, "Hold down the skin of your cock and twist the tip with the other hand."),
            M(25, "One-Direction Stroke", 8, 0, 5, 5, true, "Only stroke down/up your cock."),
            M(26, "One-Direction Finger Circle", 5, 6, 3, 3, true, "Only stroke down/up your cock using your fingers in a circle."),
            M(27, "Tip Base Twist", 3, 6, 3, 2, true, "Twist around the bottom of the tip of your cock."),
            M(28, "Fist Peek", 10, 5, 10, 1, true, "Make a fist and peek the head of your cock into it."),
            M(29, "Head Pinch Tease", 9, 6, 9, 1, true, "Tease the head of your cock with fingers in almost a pinching motion."),
            M(30, "Single Finger Tease", 1, 9, 1, 0, false, "Move one finger up and down your cock gently.")
        );

        // ── Toy methods ──
        stim.HasData(
            MT(31, "Head Vibrator (slow)", 4, 6, 3, 2, ToyCategory.HeadVibrator, "Use the penis head vibrator at a slow speed."),
            MT(32, "Head Vibrator (medium)", 6, 3, 5, 4, ToyCategory.HeadVibrator, "Use the penis head vibrator at a medium speed."),
            MT(33, "Head Vibrator (max)", 8, 2, 7, 8, ToyCategory.HeadVibrator, "Use the penis head vibrator at max speed."),
            MT(34, "Fleshlight (slow)", 6, 4, 5, 7, ToyCategory.Fleshlight, "Use the fleshlight at slow speed."),
            MT(35, "Fleshlight (medium)", 5, 5, 4, 6, ToyCategory.Fleshlight, "Use the fleshlight at medium speed."),
            MT(36, "Fleshlight (fast)", 7, 3, 3, 8, ToyCategory.Fleshlight, "Use the fleshlight at high speed."),
            MT(37, "Autoblow (slow)", 6, 6, 7, 4, ToyCategory.Autoblow, "Use the autoblow at low speed."),
            MT(38, "Autoblow (medium)", 6, 3, 5, 5, ToyCategory.Autoblow, "Use the autoblow at medium speed."),
            MT(39, "Autoblow (fast)", 7, 2, 3, 6, ToyCategory.Autoblow, "Use the autoblow at high speed."),
            MT(40, "Prostate Massager", 6, 7, 5, 4, ToyCategory.ProstateMassager, "Use the prostate massager to intensify stimulation.")
        );
    }

    private static StimMethod M(int id, string name, int intensity, int tease, int tort, int closeness, bool lube, string desc)
        => new() { Id = id, Name = name, Intensity = intensity, Tease = tease, TortLevel = tort, Closeness = closeness, RequiresLube = lube, ToyCategory = ToyCategory.None, Description = desc };

    private static StimMethod MT(int id, string name, int intensity, int tease, int tort, int closeness, ToyCategory toy, string desc)
        => new() { Id = id, Name = name, Intensity = intensity, Tease = tease, TortLevel = tort, Closeness = closeness, RequiresLube = true, ToyCategory = toy, Description = desc };

    /// <summary>Seeds punishment ender scenarios — intense finish types for punishment sessions.</summary>
    private static void SeedPunishmentEnders(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<PunishmentEnder> ender)
    {
        ender.HasData(
            PE(1, "Intense Forced Cum", 7, 6, 3, false, false, false,
                "You are going to cum tensed at maximum speed. Do NOT slow down as you cum. Keep going at full speed through the entire orgasm.",
                "You're going to cum. But you're not going to enjoy it."),
            PE(2, "Tongue Swirl CEI", 9, 8, 9, false, true, true,
                "Put your cock in your mouth. Swirl your tongue around the head. Keep swirling as you cum. Swallow every drop. If anything falls, you will be punished again.",
                "Open your mouth. You're going to need it."),
            PE(3, "Self-Oral Forced Cum", 9, 9, 10, false, true, true,
                "Put your cock as deep down your throat as you can. Use both hands on the lower shaft at maximum speed. Keep going as you cum. Swallow every drop as you are cumming. Not a single drop can fall.",
                "I hope you're flexible. You're going to swallow every last drop."),
            PE(4, "Post-Orgasm Torture (30s)", 8, 7, 4, false, false, false,
                "Cum using your normal stroke. The INSTANT you finish, switch to palming at maximum speed for 30 seconds. No stopping no matter how much it hurts.",
                "You'll get to cum. But what happens after is going to be much worse."),
            PE(5, "Post-Orgasm Torture (60s)", 10, 9, 5, false, false, false,
                "Cum using your normal stroke. The INSTANT you finish, switch to worse than palming at maximum speed tensed for 60 seconds. No stopping. No slowing down. No mercy.",
                "You're going to wish you never asked to cum."),
            PE(6, "Ruined Orgasm", 6, 5, 6, false, false, false,
                "Edge one last time. When you reach the absolute edge, let go completely. Do not touch yourself as you cum. Watch it dribble out. That's all you get.",
                "You'll get to cum. Sort of."),
            PE(7, "Bondage Intense Cum", 8, 9, 4, true, false, false,
                "Get duct tape. Tape your ankles, knees, thighs, and stomach so you cannot move. Now cum tensed at maximum speed and keep going through the entire orgasm. You can't escape it.",
                "Get the duct tape. You're not going anywhere."),
            PE(8, "Bondage Self-Oral CEI", 10, 10, 10, true, true, true,
                "Get duct tape. Tape yourself from ankles to stomach — no skin visible. Now put your cock in your mouth and cum down your throat. Keep going as you cum. Swallow everything. If a single drop falls, the punishment doubles.",
                "Tape yourself down. Every inch. You know what's coming."),
            PE(9, "Double Ruined", 7, 7, 7, false, false, false,
                "Edge. When you reach the edge, let go completely. Wait 5 seconds. Now stroke again until you reach the edge. Let go again. That's your punishment — two ruined orgasms. Nothing full.",
                "Two chances to cum. And you won't enjoy either one."),
            PE(10, "Coin Flip Punishment", 8, 10, 7, false, true, true,
                "Flip a coin. Heads: cum tensed at maximum speed and keep going for 30 seconds after. Tails: put your cock in your mouth and cum down your throat, swallowing every drop. No re-flips.",
                "Your fate is in the coin's hands now. Not mine."),
            PE(11, "Denied After Edge Marathon", 5, 8, 2, false, false, false,
                "Edge 5 more times. Slowly. Hold each edge for 10 seconds. When you're done, hands off. You don't get to cum. Walk away.",
                "Five more edges. And then? Nothing."),
            PE(12, "Fist-Peek Torture Cum", 9, 7, 8, false, false, false,
                "Make a fist. Peek the head of your cock into it until you cum. Keep peeking for 20 seconds after you cum. No full strokes allowed.",
                "Only the tip. That's all you get."),
            PE(13, "Head-Only Speed Cum", 7, 6, 6, false, false, false,
                "Stroke only the head of your cock at maximum speed until you cum. Do not touch the shaft at all. Keep stroking the head for 15 seconds after you finish.",
                "Only the head. Maximum speed. No mercy."),
            PE(14, "Slow Torture Cum", 8, 8, 5, false, false, false,
                "Stroke at speed 1. You may only cum at speed 1. If you speed up at all, you stop for 30 seconds and start over. This could take a very long time.",
                "Speed one. That's all you get. However long it takes.")
        );
    }

    private static PunishmentEnder PE(int id, string name, int severity, int anticipation, int humiliation,
        bool bondage, bool cei, bool selfOral, string instruction, string announcement)
        => new()
        {
            Id = id, Name = name, Severity = severity, Anticipation = anticipation, Humiliation = humiliation,
            RequiresBondage = bondage, RequiresCEI = cei, RequiresSelfOral = selfOral,
            InstructionText = instruction, AnnouncementText = announcement
        };
}
