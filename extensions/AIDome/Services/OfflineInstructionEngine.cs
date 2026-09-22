using AIDome.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome.Services;

public sealed class OfflineInstructionEngine : IInstructionEngine
{
    // Length parameter (1-10) → total session budget in seconds
    private static readonly int[] LengthToSeconds = [300, 450, 660, 900, 1200, 1500, 1920, 2400, 3000, 3600];

    // Phase budget ratios (fraction of total time)
    private const double WarmupRatio = 0.15;
    private const double BuildRatio = 0.45;
    private const double PeakRatio = 0.30;
    private const double FinishRatio = 0.10;

    private readonly IDbContextFactory<CommandCenterDbContext> _dbFactory;
    private readonly ILogger<OfflineInstructionEngine> _logger;

    public OfflineInstructionEngine(
        IDbContextFactory<CommandCenterDbContext> dbFactory,
        ILogger<OfflineInstructionEngine> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public Task<IReadOnlyList<InstructionStep>> GeneratePlanAsync(int sessionId, CancellationToken cancellationToken = default)
        => GeneratePlanCoreAsync(sessionId, punishment: false, cancellationToken);

    public Task<IReadOnlyList<InstructionStep>> GeneratePunishmentPlanAsync(int sessionId, CancellationToken cancellationToken = default)
        => GeneratePlanCoreAsync(sessionId, punishment: true, cancellationToken);

    public async Task<IReadOnlyList<StimMethod>> GetEligibleMethodsAsync(int sessionId, int? intensityCeiling = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        var all = await db.StimMethods.ToListAsync(cancellationToken);
        var eligible = FilterMethodsForSession(all, session);
        if (intensityCeiling.HasValue)
            eligible = eligible.Where(m => m.Intensity <= intensityCeiling.Value).ToList();
        return eligible;
    }

    private async Task<IReadOnlyList<InstructionStep>> GeneratePlanCoreAsync(int sessionId, bool punishment, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Session {sessionId} not found");

        var allMethods = await db.StimMethods.ToListAsync(cancellationToken);
        var eligible = FilterMethodsForSession(allMethods, session);

        int intensity = Math.Clamp(session.Intensity > 0 ? session.Intensity : 5, 1, 10);
        int mean = Math.Clamp(session.Mean > 0 ? session.Mean : 5, 1, 10);
        int length = Math.Clamp(session.Length > 0 ? session.Length : 5, 1, 10);
        int totalBudget = LengthToSeconds[length - 1];

        int warmupBudget = (int)(totalBudget * WarmupRatio);
        int buildBudget = (int)(totalBudget * BuildRatio);
        int peakBudget = (int)(totalBudget * PeakRatio);

        var steps = new List<InstructionStep>();
        var recentMethodIds = new List<int>();
        bool lubeActive = false;
        int ordinal = 0;

        // ── Warm-up phase ──
        int warmupSpent = 0;
        int warmupCeiling = punishment ? Math.Min(10, intensity) : Math.Min(6, intensity);

        while (warmupSpent < warmupBudget)
        {
            var m = PickMethod(eligible, recentMethodIds, warmupCeiling, ref lubeActive, preferTease: true);
            int speed = Random.Shared.Next(1, 4);

            InstructionStep step;
            if (ShouldUseTimed(m) || m.Closeness < 3)
            {
                int dur = Jitter(Random.Shared.Next(30, 80));
                step = MakeStep(sessionId, ordinal++, InstructionKind.TimedMethod, m, speed, dur,
                    $"{Desc(m)}. Speed {speed}. {dur} seconds. Take it slow.");
                warmupSpent += dur;
            }
            else
            {
                int target = 1;
                step = MakeStep(sessionId, ordinal++, InstructionKind.EdgeCount, m, speed, target,
                    $"{Desc(m)}. Speed {speed}. Edge {target} time(s).");
                warmupSpent += 60; // estimate ~60s per edge for budgeting
            }
            steps.Add(step);
            TrackMethod(recentMethodIds, m, ref lubeActive);
        }

        // ── Build phase ──
        int buildSpent = 0;
        int buildCeiling = Math.Min(10, intensity + (punishment ? 2 : 0));
        int stepsSinceRest = 0;

        while (buildSpent < buildBudget)
        {
            // Rest insertion
            if (stepsSinceRest >= (punishment ? 5 : 3))
            {
                int restDur = ComputeRestDuration(intensity, mean, punishment);
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.Rest, null, 0, restDur,
                    FormatRest(mean, punishment, restDur)));
                buildSpent += restDur;
                stepsSinceRest = 0;
                continue;
            }

            var m = PickMethod(eligible, recentMethodIds, buildCeiling, ref lubeActive);
            int speed = Random.Shared.Next(3, Math.Min(10, buildCeiling + 1));

            if (ShouldUseTimed(m))
            {
                int dur = Jitter(Random.Shared.Next(45, 120));
                // High-closeness torture/tease methods: note to edge as many times as possible
                string edgeNote = m.Closeness >= 4
                    ? $" Edge as many times as you can during. Stop counting when you aren't stroking."
                    : "";
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.TimedMethod, m, speed, dur,
                    $"{Desc(m)}. Speed {speed}. {dur} seconds.{edgeNote}"));
                buildSpent += dur;
            }
            else
            {
                int target = punishment ? 2 + Random.Shared.Next(3) : 1 + Random.Shared.Next(2);
                string holdText = FormatHoldText(mean, punishment, target);
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.EdgeCount, m, speed, target,
                    $"{Desc(m)}. Speed {speed}. Edge {target} time(s).{holdText}"));
                buildSpent += target * 60;
            }
            TrackMethod(recentMethodIds, m, ref lubeActive);
            stepsSinceRest++;
        }

        // ── Peak phase ──
        int peakSpent = 0;
        int peakCeiling = Math.Min(10, intensity + 2);
        stepsSinceRest = 0;

        while (peakSpent < peakBudget)
        {
            // Rest insertion (less frequent in peak)
            if (stepsSinceRest >= (punishment ? 6 : 4))
            {
                int restDur = ComputeRestDuration(intensity, mean, punishment);
                // Peak rests are shorter
                restDur = Math.Max(5, restDur * 2 / 3);
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.Rest, null, 0, restDur,
                    FormatRest(mean, punishment, restDur)));
                peakSpent += restDur;
                stepsSinceRest = 0;
                continue;
            }

            bool doTorment = punishment
                ? Random.Shared.Next(100) < 50  // 50% torment in punishment
                : Random.Shared.Next(100) < 20; // 20% normally

            if (doTorment)
            {
                var m = PickMethod(eligible, recentMethodIds, peakCeiling, ref lubeActive, preferTorture: true);
                int speed = Random.Shared.Next(6, 10);
                int dur = punishment ? Jitter(Random.Shared.Next(45, 120)) : Jitter(Random.Shared.Next(30, 90));
                string opener = punishment ? "Punishment time. " : "";
                string edgeNote = m.Closeness >= 4
                    ? $" Edge as many times as you can during. Stop counting when you aren't stroking."
                    : "";
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.TimedMethod, m, speed, dur,
                    $"{opener}{Desc(m)}. Speed {speed}. {dur} seconds. No stopping.{edgeNote}"));
                peakSpent += dur;
                TrackMethod(recentMethodIds, m, ref lubeActive);
            }
            else
            {
                var m = PickMethod(eligible, recentMethodIds, peakCeiling, ref lubeActive, preferCloseness: true);
                int speed = Random.Shared.Next(5, Math.Min(10, peakCeiling + 2));
                int target = punishment ? 3 + Random.Shared.Next(4) : 2 + Random.Shared.Next(3);
                string holdText = FormatHoldText(mean, punishment, target);
                steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.EdgeCount, m, speed, target,
                    $"{Desc(m)}. Speed {speed}. Edge {target} time(s).{holdText}"));
                peakSpent += target * 60;
                TrackMethod(recentMethodIds, m, ref lubeActive);
            }
            stepsSinceRest++;
        }

        // ── Finish/Deny ──
        var enders = await db.PunishmentEnders.ToListAsync(cancellationToken);
        if (punishment)
        {
            steps.AddRange(BuildPunishmentFinish(eligible, recentMethodIds, ref lubeActive, sessionId, ref ordinal, intensity, mean, enders, session));
        }
        else
        {
            var finishMethod = PickMethod(eligible, recentMethodIds, intensity, ref lubeActive);
            steps.Add(MakeStep(sessionId, ordinal++, InstructionKind.Finish, finishMethod, 5, 0,
                $"{DommeText.Pick(DommeText.FinishDenied)} {Desc(finishMethod)}."));
        }

        // Persist all steps
        db.InstructionSteps.AddRange(steps);
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offline plan generated for session {Id} (punishment={Punishment}). {Count} steps, budget={Budget}s",
            sessionId, punishment, steps.Count, totalBudget);

        return steps;
    }

    // ──────────────────── Punishment finishes ────────────────────

    private static List<InstructionStep> BuildPunishmentFinish(
        List<StimMethod> eligible, List<int> recentIds, ref bool lubeActive,
        int sessionId, ref int ordinal, int intensity, int mean,
        List<PunishmentEnder> enders, SessionRecord session)
    {
        var result = new List<InstructionStep>();

        // Filter enders by session kink flags
        var pool = enders.Where(e =>
        {
            if (e.RequiresBondage && !session.AllowBondage) return false;
            if (e.RequiresCEI && !session.AllowCEI) return false;
            if (e.RequiresSelfOral && !session.AllowSelfOral) return false;
            return true;
        }).ToList();

        if (pool.Count == 0)
        {
            // Fallback: simple denial
            result.Add(MakeStep(sessionId, ordinal++, InstructionKind.Finish, null, 0, 0,
                DommeText.Pick(DommeText.FinishDenied)));
            return result;
        }

        // Weighted selection: higher mean → weighted toward higher severity
        var ender = MethodSelector.WeightedSelect(pool, e =>
        {
            double w = 1.0 + e.Severity * (mean / 5.0);
            if (e.Anticipation >= 7) w *= 1.3;
            return w;
        });

        // Build the announcement + instruction steps
        if (!string.IsNullOrWhiteSpace(ender.AnnouncementText))
        {
            result.Add(MakeStep(sessionId, ordinal++, InstructionKind.FreeText, null, 0, 10,
                ender.AnnouncementText));
        }

        // Pick a method for the ender (using the instruction text as template)
        var m = PickMethod(eligible, recentIds, Math.Min(10, intensity + 3), ref lubeActive, preferTorture: ender.Severity >= 7, preferCloseness: ender.Severity < 5);
        string text = ender.InstructionText.Replace("{method}", Desc(m));
        result.Add(MakeStep(sessionId, ordinal++, InstructionKind.Finish, m, Random.Shared.Next(5, 10), 0, text));

        return result;
    }

    // ──────────────────── Helpers ────────────────────

    private static bool ShouldUseTimed(StimMethod m)
    {
        if (m.TortLevel >= 5 && m.Closeness <= 3) return true;
        if (m.Tease >= 6 && m.Closeness <= 3) return true;
        return false;
    }

    private static int ComputeRestDuration(int intensity, int mean, bool punishment)
    {
        int baseDur = mean >= 7 || punishment ? 10 : 20;
        int dur = baseDur + Random.Shared.Next(5, 20);
        dur = Math.Max(5, (int)(dur * (1.5 - mean / 10.0)));
        if (punishment) dur = Math.Max(5, dur / 2);
        return dur;
    }

    private static string FormatRest(int mean, bool punishment, int dur)
    {
        return $"{DommeText.PickRest(mean, punishment)} {dur} seconds.";
    }

    // ── Configurable edge hold limits ──
    private const int MinHoldSecondsNormal = 5;
    private const int MaxHoldSecondsNormal = 20;
    private const int MinHoldSecondsPunishment = 8;
    private const int MaxHoldSecondsPunishment = 25;

    private static string FormatHoldText(int mean, bool punishment, int edgeTarget)
    {
        int holdChance = punishment ? 60 : 35;
        if (Random.Shared.Next(100) >= holdChance)
            return "";

        int minH = punishment ? MinHoldSecondsPunishment : MinHoldSecondsNormal;
        int maxH = punishment ? MaxHoldSecondsPunishment : MaxHoldSecondsNormal;
        int hold = minH + Random.Shared.Next(maxH - minH + 1);
        hold = Math.Min(maxH, (int)(hold * (1.0 + mean / 40.0)));

        if (edgeTarget > 1)
            return $" Hold at the edge for {hold} seconds on the last edge.";
        return $" Hold at the edge for {hold} seconds.";
    }

    private static InstructionStep MakeStep(int sessionId, int ordinal, InstructionKind kind, StimMethod? method, int speed, int durationOrCount, string text)
    {
        return new InstructionStep
        {
            SessionId = sessionId,
            Ordinal = ordinal,
            Kind = kind,
            StimMethodId = method?.Id,
            Speed = speed,
            DurationOrCount = durationOrCount,
            Text = text
        };
    }

    private static int Jitter(int value)
    {
        double factor = 1.0 + (Random.Shared.NextDouble() - 0.5) * 0.4;
        return Math.Max(10, (int)(value * factor));
    }

    /// <summary>Get description text suitable for embedding — strips trailing punctuation.</summary>
    private static string Desc(StimMethod m)
    {
        string d = m.Description.TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(d) ? m.Name : d;
    }

    private static StimMethod PickMethod(List<StimMethod> eligible, List<int> recentIds, int intensityCeiling, ref bool lubeActive,
        bool preferTease = false, bool preferTorture = false, bool preferCloseness = false)
    {
        var pool = eligible.Where(m => m.Intensity <= intensityCeiling).ToList();
        if (pool.Count == 0) pool = eligible.ToList();
        if (pool.Count == 0) return FallbackMethod();

        // Hard lube enforcement: once lube is active, only pick lube methods.
        // If no lube methods satisfy the intensity ceiling, expand to all eligible lube methods.
        if (lubeActive)
        {
            var lubePool = pool.Where(m => m.RequiresLube).ToList();
            if (lubePool.Count == 0)
                lubePool = eligible.Where(m => m.RequiresLube).ToList();
            if (lubePool.Count > 0) pool = lubePool;
        }

        bool lube = lubeActive;
        int lastId = recentIds.Count > 0 ? recentIds[^1] : -1;
        var weighted = pool.Select(m =>
        {
            int idx = recentIds.LastIndexOf(m.Id);
            double w = idx >= 0 ? 1.0 / (1 + (recentIds.Count - idx)) : 2.0;
            // Strongly avoid immediate repeats
            if (m.Id == lastId && pool.Count > 1) w *= 0.02;
            if (preferTease) w *= 1.0 + m.Tease / 10.0;
            if (preferTorture) w *= 1.0 + m.TortLevel / 10.0;
            if (preferCloseness) w *= 1.0 + m.Closeness / 5.0;
            // If lube is NOT active, mildly suppress lube methods (avoid forcing lube early)
            // Note: lube=true is handled via hard filter above
            if (!lube && m.RequiresLube) w *= 0.4;
            return (m, w);
        }).ToList();

        double total = weighted.Sum(x => x.w);
        double roll = Random.Shared.NextDouble() * total;
        double cum = 0;
        foreach (var (m, w) in weighted)
        {
            cum += w;
            if (roll <= cum) return m;
        }

        return pool[Random.Shared.Next(pool.Count)];
    }

    private static void TrackMethod(List<int> recentIds, StimMethod method, ref bool lubeActive)
    {
        recentIds.Add(method.Id);
        if (recentIds.Count > 10)
            recentIds.RemoveAt(0);
        if (method.RequiresLube) lubeActive = true;
    }

    private static StimMethod FallbackMethod() => new()
    {
        Id = 0,
        Name = "Standard stroke",
        Description = "Use your dominant hand at a comfortable pace."
    };

    private static List<StimMethod> FilterMethodsForSession(List<StimMethod> all, SessionRecord s)
    {
        return all.Where(m =>
        {
            if (m.ToyCategory == ToyCategory.None) return true;
            if (!s.AllowToys) return false;
            return m.ToyCategory switch
            {
                ToyCategory.HeadVibrator => s.AllowVibrator,
                ToyCategory.Fleshlight => s.AllowFleshlight,
                ToyCategory.Autoblow => s.AllowAutoblow,
                ToyCategory.ProstateMassager => s.AllowProstate,
                _ => false
            };
        }).ToList();
    }
}
