using System.Text.Json;
using AIDome.Data;
using AIDome.Persistence;
using AIDome.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIDome.Sessions;

public record ChatMessage(Guid Id, DateTime Timestamp, string Sender, string Content);
public record ResolvedDefaults(int Intensity, int Length, int Denial, int Mean);
/// <summary>The running game as the user sees it: never the secret, only how to answer.</summary>
public sealed record GameView(GameType Type, string Name, GameInput Input, IReadOnlyList<GameChoice> Choices);

/// <summary>What the page and companion show about the running session.</summary>
public sealed record SessionView
{
    public int SessionId { get; init; }
    public int StepNumber { get; init; }
    public bool IsPaused { get; init; }
    public bool IsDebugSession { get; init; }
    public SessionMode Mode { get; init; }
    public SessionContract Contract { get; init; } = new();
    public int ContractRevision { get; init; }
    public Directive? CurrentDirective { get; init; }
    /// <summary>Seconds left in the current instruction; -1 when it has no visible countdown.</summary>
    public int RemainingSeconds { get; init; }
    /// <summary>Edges still to reach for the current target, when there is one.</summary>
    public int? EdgesRemaining { get; init; }
    public int EdgesThisSession { get; init; }
    public int EdgesThisStep { get; init; }
    public int ElapsedSeconds { get; init; }
    public string Mood { get; init; } = "";
    public SessionPhase Phase { get; init; }
    public GameView? Game { get; init; }
    public bool PermissionGranted { get; init; }
    public bool CanRequestPermission { get; init; }
    public bool CanRequestRest { get; init; }
    /// <summary>"Done" applies: ready after applying lube, or acknowledging a denial.</summary>
    public bool CanComplete { get; init; }
    /// <summary>The metronome tempo she is setting right now (0 = silent or not hers to set).</summary>
    public int Tempo { get; init; }
    /// <summary>Her deal on the table, if any.</summary>
    public OfferView? Offer { get; init; }
    /// <summary>What the user still owes this session.</summary>
    public int OwedEdges { get; init; }
    public int OwedRuins { get; init; }
    public bool OwesPunishmentEnding { get; init; }
    /// <summary>No orgasm before this time; <see cref="DateTime.MaxValue"/> for the whole session.</summary>
    public DateTime? NoOrgasmUntilUtc { get; init; }
    /// <summary>Stopped with nobody here; she asks what happened before it can resume.</summary>
    public bool Interrupted { get; init; }
}

public sealed record OfferView(Guid Id, OfferBuys Buys, OfferKind Kind, string Text, int SecondsLeft);

/// <summary>
/// Owns one account's running session: applies engine transitions under a lock, persists what they
/// produce, keeps the chat log, and arms the one session timer. Callers serialize through the
/// account runtime's gate; the lock only protects reads from other threads.
/// </summary>
public sealed class SessionHost : IDisposable
{
    private const int MaxMessages = 200;
    private static readonly JsonSerializerOptions Json = GuidedRecovery.Json;
    private readonly object sync = new();
    private readonly GuidedDataFactory data;
    private readonly IInstructionEngine homework;
    private readonly ILogger logger;
    private readonly List<ChatMessage> messages = [];
    private readonly HashSet<Guid> consumed = [];
    private Timer? timer;
    private long armedGeneration = -1;
    private bool disposed;
    private DateTime lastSeenUtc;

    /// <summary>A running session nobody has looked at for this long stops, as of the last visit.</summary>
    public static readonly TimeSpan UnattendedAfter = TimeSpan.FromMinutes(3);

    public SessionHost(GuidedDataFactory data, IInstructionEngine homework, ILogger logger, IRandom? random = null, IClock? clock = null,
        Func<IRandom, Authorities, IPlanner>? planners = null)
    {
        this.data = data;
        this.homework = homework;
        this.logger = logger;
        Engine = new SessionEngine(random ?? SystemRandom.Instance, clock ?? SystemClock.Instance, planners);
        Ledger = new LedgerStore(data, this);
        lastSeenUtc = Engine.Clock.UtcNow;
    }

    public SessionEngine Engine { get; }
    public LedgerStore Ledger { get; }

    /// <summary>Set by the account runtime so timer transitions take the same gate as requests.</summary>
    public Func<Func<Task>, Task>? TimerDispatcher { get; init; }

    public SessionState? State { get; private set; }
    public bool Active { get { lock (sync) return State?.Active == true; } }
    /// <summary>Whether an orgasm recorded now is allowed.</summary>
    public bool PermissionStands { get { lock (sync) return State is { Active: true, PermissionGranted: true }; } }
    public int? ActiveSessionId { get { lock (sync) return State is { Active: true } s ? s.SessionId : null; } }
    public int StepNumber { get { lock (sync) return State?.StepNumber ?? 0; } }
    public bool Paused { get { lock (sync) return State?.Paused == true; } }

    // ── Lifecycle ──

    public async Task StartAsync(SessionContract requested, CancellationToken ct)
    {
        if (Active) throw new InvalidOperationException("The current session isn't over yet.");
        var contract = requested.Validate() with { Obligations = new() };
        if (!contract.Debug) await Ledger.ReconcileAsync(ct, force: true);
        var now = Engine.Clock.UtcNow;
        await using var db = data.Open();
        if (!contract.Debug && await db.FollowUps.AnyAsync(x => x.Status == FollowUpStatus.Open, ct))
            throw new InvalidOperationException("Answer her questions before you start.");
        var defaults = await ResolvedDefaultsAsync(db, ct);
        var p = contract.Preferences;
        contract = contract with
        {
            Preferences = new()
            {
                Intensity = p.Intensity > 0 ? p.Intensity : defaults.Intensity, Length = p.Length > 0 ? p.Length : defaults.Length,
                Denial = p.Denial > 0 ? p.Denial : defaults.Denial, Mean = p.Mean > 0 ? p.Mean : defaults.Mean,
            },
        };
        var library = (await db.MethodsAsync(ct)).Select(MethodInfo.From).ToList();
        if (contract.Limits.AllowedMethodIds.Any(id => library.All(m => m.Id != id))) throw new ArgumentException("An allowed method does not exist.");
        var methods = library.Where(contract.Allows).ToList();
        if (contract.Methods == Authority.Domme && methods.Count == 0) throw new ArgumentException("No methods match these limits.");
        var metrics = await data.ReadTrackerMetricsAsync(now, ct);

        var record = db.Add(new SessionRecord { StartedAtUtc = now, LastInteractionUtc = now, Kink = -1, IsDebugSession = contract.Debug });
        Describe(record, contract);
        await db.SaveChangesAsync(ct);
        // What the user owes is folded in; a debug session never carries obligations.
        if (!contract.Debug)
        {
            contract = LedgerStore.Apply(contract, await LedgerStore.FoldAsync(db, record.Id, now, ct));
            Describe(record, contract);
            await db.SaveChangesAsync(ct);
        }
        await RecordContractAsync(db, record.Id, 1, contract, ct);

        var limits = contract.Limits;
        var enders = contract.Obligations.PunishmentEnding
            ? (await db.EndersAsync(ct)).Where(e => (!e.RequiresBondage || limits.Bondage) && (!e.RequiresCEI || limits.Cei) && (!e.RequiresSelfOral || limits.SelfOral))
                .Select(EnderInfo.From).ToList()
            : [];
        var offers = (await db.OfferDefinitionsAsync(ct)).Where(x => x.Enabled).Select(OfferTemplate.From).ToList();
        var state = new SessionState
        {
            SessionId = record.Id, Contract = contract, Methods = methods, Enders = enders, OfferTemplates = offers,
            HoursSinceLastOrgasmAtStart = metrics.LastOrgasmAtUtc is DateTime last ? (now - last).TotalHours : null,
        };
        Transition t;
        lock (sync)
        {
            State = state;
            consumed.Clear();
            t = Engine.Begin(state);
        }
        Notice($"Session {record.Id} started: {ModeName(contract.Mode)}.");
        await FinishAsync(t, ct);
    }

    private static void Describe(SessionRecord record, SessionContract contract)
    {
        var limits = contract.Limits;
        var p = contract.Preferences;
        record.Intensity = p.Intensity;
        record.Length = p.Length;
        record.Denial = p.Denial;
        record.Mean = p.Mean;
        record.AllowToys = limits.Toys.Length > 0;
        record.AllowVibrator = limits.Toys.Contains(ToyCategory.HeadVibrator);
        record.AllowFleshlight = limits.Toys.Contains(ToyCategory.Fleshlight);
        record.AllowAutoblow = limits.Toys.Contains(ToyCategory.Autoblow);
        record.AllowProstate = limits.Toys.Contains(ToyCategory.ProstateMassager);
        record.AllowAnal = limits.Anal;
        record.AllowBondage = limits.Bondage;
        record.AllowCEI = limits.Cei;
        record.AllowSelfOral = limits.SelfOral;
        record.Mode = contract.Mode.ToString();
        record.ContractJson = JsonSerializer.Serialize(contract, Json);
    }

    public async Task ChangeContractAsync(int sessionId, int revision, SessionContract requested, CancellationToken ct)
    {
        var state = State;
        if (state is not { Active: true } || state.SessionId != sessionId || state.ContractRevision != revision)
            throw new InvalidOperationException("Session settings changed elsewhere. Refresh before saving.");
        var contract = requested.Validate();
        await using var db = data.Open();
        var library = (await db.MethodsAsync(ct)).Select(MethodInfo.From).ToList();
        if (contract.Methods == Authority.Domme && !library.Any(contract.Allows)) throw new ArgumentException("No methods match these limits.");
        Transition t;
        lock (sync) t = Engine.ChangeContract(state, contract, library);
        await RecordContractAsync(db, state.SessionId, state.ContractRevision, state.Contract, ct);
        var record = await db.Sessions.SingleAsync(x => x.Id == state.SessionId, ct);
        record.Mode = state.Contract.Mode.ToString();
        record.ContractJson = JsonSerializer.Serialize(state.Contract, Json);
        await db.SaveChangesAsync(ct);
        await FinishAsync(t, ct);
    }

    /// <summary>A session control from the page, companion or a hotkey.</summary>
    public Task ControlAsync(string action, int? answer, CancellationToken ct) => action switch
    {
        "pause" => ApplyAsync(Engine.Pause, ct),
        "resume" => ApplyAsync(Engine.Resume, ct),
        "done" => ApplyAsync(Engine.Complete, ct),
        "request-permission" => ApplyAsync(Engine.RequestPermission, ct),
        "request-rest" => ApplyAsync(Engine.RequestRest, ct),
        "game-answer" => ApplyAsync(s => Engine.Answer(s, answer ?? throw new ArgumentException("An answer is required.")), ct),
        "offer-accept" => ApplyAsync(Engine.AcceptOffer, ct),
        "offer-decline" => ApplyAsync(Engine.DeclineOffer, ct),
        "end" => ApplyAsync(s => Engine.End(s, SessionOutcome.Unknown), ct),
        _ => throw new ArgumentException("Unknown session control."),
    };

    public async Task ReportInfractionAsync(InfractionType type, string? note, CancellationToken ct)
    {
        var state = State;
        if (state is not { Active: true }) throw new InvalidOperationException("No active session.");
        await using (var db = data.Open())
        {
            db.Add(new InfractionEntry { TimestampUtc = Engine.Clock.UtcNow, SessionId = state.SessionId, Type = type, Notes = note ?? "" });
            await db.SaveChangesAsync(ct);
        }
        await ApplyAsync(s => Engine.Infraction(s, type), ct);
    }

    /// <summary>A tracker event recorded during the session; each event is applied at most once.</summary>
    public async Task ConsumeTrackerEventAsync(Guid eventId, string kind, bool? allowedOrgasm, CancellationToken ct)
    {
        lock (sync)
            if (!consumed.Add(eventId)) return;
        if (kind == "edge") await ApplyAsync(Engine.Edge, ct);
        else await ApplyAsync(s => Engine.Orgasm(s, allowedOrgasm ?? s.PermissionGranted), ct);
    }

    public async Task ObserveEarlierEdgeAsync(Guid eventId, CancellationToken ct)
    {
        lock (sync)
            if (!consumed.Add(eventId)) return;
        await ApplyAsync(Engine.EarlierEdge, ct);
    }

    /// <summary>A correction (undo) touched this session's facts. Guidance stops rather than rewriting the past.</summary>
    public async Task StopAfterTrackingCorrectionAsync(CancellationToken ct)
    {
        if (!Active) return;
        await ApplyAsync(s => Engine.End(s, SessionOutcome.Unknown), ct);
        Notice("Guidance stopped after a tracking correction. Recorded counts were corrected; start a new session to continue.");
    }

    /// <summary>Ends the session with an outcome decided outside the engine (an answer about an interruption).</summary>
    public Task EndAsync(SessionOutcome outcome, CancellationToken ct) => ApplyAsync(s => Engine.End(s, outcome), ct);

    /// <summary>A page or the companion checked in: someone is here.</summary>
    public void Seen() { lock (sync) lastSeenUtc = Engine.Clock.UtcNow; }

    /// <summary>A running session nobody has looked at for a while stops as of the last visit.</summary>
    public async Task InterruptIfUnattendedAsync(CancellationToken ct)
    {
        DateTime seen;
        lock (sync)
        {
            if (!Unattended(State)) return;
            seen = lastSeenUtc;
        }
        await ApplyAsync(s => Engine.Interrupt(s, seen), ct);
    }

    /// <summary>Cove is stopping: the session stops now, and she asks about it next time.</summary>
    public async Task InterruptAsync(CancellationToken ct)
    {
        if (Active && !Paused) await ApplyAsync(s => Engine.Interrupt(s, Engine.Clock.UtcNow), ct);
    }

    /// <summary>A session left paused for too long is treated as interrupted.</summary>
    public async Task InterruptLongPauseAsync(TimeSpan after, CancellationToken ct)
    {
        DateTime pausedAt;
        lock (sync)
        {
            if (State is not { Active: true, Paused: true, InterruptedAtUtc: null, PausedAtUtc: DateTime at } s || s.Contract.Debug
                || Engine.Clock.UtcNow - at < after) return;
            pausedAt = at;
        }
        await ApplyAsync(s => Engine.Interrupt(s, pausedAt), ct);
    }

    public void ClearInterruption() { lock (sync) if (State is { } s) Engine.ClearInterruption(s); }

    private bool Unattended(SessionState? s)
        => s is { Active: true, Paused: false } && !s.Contract.Debug && Engine.Clock.UtcNow - lastSeenUtc > UnattendedAfter;

    /// <summary>Ends without any outcome (recovery found an interrupted command).</summary>
    public void Abandon(string notice)
    {
        lock (sync)
            if (State is { Active: true } s) Engine.End(s, SessionOutcome.Unknown);
        Disarm();
        Notice(notice);
    }

    public async Task<IReadOnlyList<InstructionStep>> GenerateHomeworkAsync(SessionContract requested, bool punishment, CancellationToken ct)
    {
        var contract = requested.Validate();
        var now = Engine.Clock.UtcNow;
        await using var db = data.Open();
        var defaults = await ResolvedDefaultsAsync(db, ct);
        var p = contract.Preferences;
        var limits = contract.Limits;
        var record = db.Add(new SessionRecord
        {
            StartedAtUtc = now, LastInteractionUtc = now, Intensity = p.Intensity > 0 ? p.Intensity : defaults.Intensity,
            Length = p.Length > 0 ? p.Length : defaults.Length, Denial = p.Denial > 0 ? p.Denial : defaults.Denial,
            Mean = p.Mean > 0 ? p.Mean : defaults.Mean, Kink = -1, AllowToys = limits.Toys.Length > 0,
            AllowVibrator = limits.Toys.Contains(ToyCategory.HeadVibrator), AllowFleshlight = limits.Toys.Contains(ToyCategory.Fleshlight),
            AllowAutoblow = limits.Toys.Contains(ToyCategory.Autoblow), AllowProstate = limits.Toys.Contains(ToyCategory.ProstateMassager),
            AllowAnal = limits.Anal, AllowBondage = limits.Bondage, AllowCEI = limits.Cei, AllowSelfOral = limits.SelfOral,
            IsDebugSession = true, Mode = "Homework", ContractJson = JsonSerializer.Serialize(contract, Json),
        });
        await db.SaveChangesAsync(ct);
        var steps = punishment ? await homework.GeneratePunishmentPlanAsync(record.Id, ct) : await homework.GeneratePlanAsync(record.Id, ct);
        foreach (var step in steps) AddMessage("Domme", $"{(punishment ? "Punishment" : "Homework")}: {step.Text}");
        return steps;
    }

    // ── Reads ──

    public IReadOnlyList<ChatMessage> Messages() { lock (sync) return messages.ToList(); }
    public Guid[] ConsumedEvents() { lock (sync) return consumed.ToArray(); }

    public SessionView? View()
    {
        lock (sync)
        {
            if (State is not { Active: true } s) return null;
            var now = Engine.Clock.UtcNow;
            var a = SessionEngine.AuthoritiesFor(s, Engine.Random);
            var d = s.Current;
            int remaining = d?.HiddenTimer == true ? -1
                : s.Paused ? (s.PausedTimerSeconds > 0 ? s.PausedTimerSeconds : -1)
                : s.TimerDueUtc is DateTime due ? Math.Max(0, (int)Math.Ceiling((due - now).TotalSeconds)) : -1;
            int? edgesRemaining = s.Price is { } price && !s.Holding ? price.EdgesLeft
                : d is { Kind: InstructionKind.EdgeCount } && !s.Holding ? Math.Max(0, d.DurationOrCount - s.EdgesThisStep)
                : s.Game is { Type: GameType.EdgeRush or GameType.EdgeWager } g && (g.Type == GameType.EdgeRush || g.Phase == 1)
                    ? Math.Max(0, g.Target - (s.EdgesThisStep - g.EdgesAtPhaseStart)) : null;
            return new()
            {
                SessionId = s.SessionId, StepNumber = s.StepNumber, IsPaused = s.Paused, IsDebugSession = s.Contract.Debug,
                Mode = s.Contract.Mode, Contract = s.Contract, ContractRevision = s.ContractRevision, CurrentDirective = d,
                RemainingSeconds = remaining, EdgesRemaining = edgesRemaining, EdgesThisSession = s.EdgesThisSession, EdgesThisStep = s.EdgesThisStep,
                ElapsedSeconds = (int)Math.Max(0, ((s.Paused ? s.PausedAtUtc ?? now : now) - s.StartedAtUtc).TotalSeconds - s.PausedSeconds),
                Mood = s.Mood.Mood.ToString(), Phase = d?.Phase ?? SessionPhase.Building,
                Game = s.Game is { } game ? new(game.Type, GameName(game.Type), game.Input, game.Choices) : null,
                PermissionGranted = s.PermissionGranted,
                CanRequestPermission = !s.FinishDecided && s.Game is null && s.Offer is null && s.Price is null,
                CanRequestRest = a.Pacing.DommeSets && !s.FinishDecided && s.Game is null && s.Offer is null && s.Price is null
                    && d?.Kind != InstructionKind.Rest && !s.Holding,
                CanComplete = d?.Kind == InstructionKind.Finish || (s.PendingLube is not null && d?.Category == StepCategory.Rest && s.TimerDueUtc is null),
                Tempo = s.Paused ? 0 : d?.Tempo ?? 0,
                Offer = s.Offer is { } offer && (offer.Buys == OfferBuys.Mercy || offer.ExpiresAtUtc > now)
                    ? new(offer.Id, offer.Buys, offer.Kind, offer.Text,
                        offer.Buys == OfferBuys.Mercy ? Math.Max(0, remaining) : Math.Max(0, (int)Math.Ceiling((offer.ExpiresAtUtc - now).TotalSeconds)))
                    : null,
                OwedEdges = s.OwedEdges, OwedRuins = s.OwedRuins, OwesPunishmentEnding = s.OwedEndings > 0,
                NoOrgasmUntilUtc = s.DenialUntilUtc is DateTime until && until > now ? until : null,
                Interrupted = s.InterruptedAtUtc is not null,
            };
        }
    }

    public DateTime? StepEndsAtUtc()
    {
        lock (sync)
            return State is { Active: true, Paused: false, TimerDueUtc: DateTime due } s && s.Current?.HiddenTimer != true ? due : null;
    }

    /// <summary>What an edge recorded now is attributed to (method and prescribed speed).</summary>
    public (int MethodId, int Speed) EdgeAttribution()
    {
        lock (sync)
        {
            var d = State?.Current;
            return (d?.Method is { IsUserChoice: false } m ? m.Id : 1, d?.Speed ?? 0);
        }
    }

    public async Task<ResolvedDefaults> ResolvedDefaultsAsync(CancellationToken ct)
    {
        await using var db = data.Open();
        return await ResolvedDefaultsAsync(db, ct);
    }

    private static async Task<ResolvedDefaults> ResolvedDefaultsAsync(GuidedData db, CancellationToken ct)
    {
        var recent = await db.Sessions.Where(x => x.Mode != "Homework").OrderByDescending(s => s.StartedAtUtc).Take(5).ToListAsync(ct);
        if (recent.Count == 0) return new(5, 5, 5, 5);
        static int Avg(IEnumerable<int> values) => Math.Clamp((int)Math.Round(values.Average(v => v > 0 ? v : 5)), 1, 10);
        return new(Avg(recent.Select(s => s.Intensity)), Avg(recent.Select(s => s.Length)), Avg(recent.Select(s => s.Denial)), Avg(recent.Select(s => s.Mean)));
    }

    public async Task<IReadOnlyList<SessionRecord>> HistoryAsync(int count, CancellationToken ct)
    {
        await using var db = data.Open();
        return await db.Sessions.AsNoTracking().OrderByDescending(s => s.StartedAtUtc).Take(Math.Clamp(count, 1, 100)).ToListAsync(ct);
    }

    // ── Recovery ──

    public string? ExportState() { lock (sync) return State is { Active: true } s ? JsonSerializer.Serialize(s, Json) : null; }

    /// <summary>Restores a saved session paused: its countdown as of the save, no missed timers replayed.</summary>
    public void Restore(string? stateJson, DateTime savedAtUtc, IEnumerable<ChatMessage> saved, IEnumerable<Guid> consumedEvents)
    {
        lock (sync)
        {
            messages.Clear();
            messages.AddRange(saved.TakeLast(MaxMessages));
            consumed.Clear();
            foreach (var id in consumedEvents) consumed.Add(id);
            State = null;
            if (stateJson is null) return;
            var s = JsonSerializer.Deserialize<SessionState>(stateJson, Json) ?? throw new InvalidOperationException("Invalid saved session.");
            if (s.Format != 2) throw new InvalidOperationException("Unsupported saved session version.");
            if (s.Active && !s.Paused)
            {
                s.Paused = true;
                s.PausedAtUtc = savedAtUtc;
                s.PausedTimerSeconds = s.TimerDueUtc is DateTime due ? Math.Max(1, (int)Math.Ceiling((due - savedAtUtc).TotalSeconds)) : 0;
                s.TimerDueUtc = null;
                s.TimerGeneration++;
                s.ActiveSinceUtc = null;
                // It was running when Cove stopped: she will ask what happened.
                if (!s.Contract.Debug) s.InterruptedAtUtc ??= savedAtUtc;
            }
            State = s.Active ? s : null;
        }
    }

    public void RestoreMessages(IEnumerable<ChatMessage> saved, IEnumerable<Guid> consumedEvents) => Restore(null, default, saved, consumedEvents);

    // ── Internals ──

    private async Task ApplyAsync(Func<SessionState, Transition> action, CancellationToken ct)
    {
        Transition t;
        lock (sync)
        {
            if (State is null) throw new InvalidOperationException("No active session.");
            t = action(State);
        }
        await FinishAsync(t, ct);
    }

    /// <summary>Persists a transition's effects, posts its lines and arms the timer.</summary>
    private async Task FinishAsync(Transition t, CancellationToken ct)
    {
        if (t.Effects.Count > 0)
        {
            try { await PersistAsync(t.Effects, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Could not save part of the session record");
                Notice("Part of this session's record could not be saved. The session continues.");
            }
        }
        foreach (var line in t.Domme) AddMessage("Domme", line);
        foreach (var line in t.Notices) AddMessage("Manager", line);
        if (t.Rejected is not null) AddMessage("System", t.Rejected);
        Arm();
    }

    private async Task PersistAsync(IReadOnlyList<SessionEffect> effects, CancellationToken ct)
    {
        int sessionId;
        bool debug;
        lock (sync) (sessionId, debug) = (State?.SessionId ?? 0, State?.Contract.Debug ?? true);
        await using var db = data.Open();
        var now = Engine.Clock.UtcNow;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case GameStarted started:
                {
                    var row = db.Add(new GameChallenge
                    {
                        SessionId = sessionId, IssuedAtUtc = now, GameType = started.Game.Type, CorrectAnswer = started.Game.Secret,
                        Prompt = Truncate(started.Game.Prompt, 1024), SuccessReward = Truncate(Games.Describe(started.Game.Win), 512),
                        FailureConsequence = Truncate(Games.Describe(started.Game.Loss), 512),
                    });
                    await db.SaveChangesAsync(ct);
                    lock (sync) started.Game.RecordId = row.Id;
                    break;
                }
                case GameFinished finished when finished.Game.RecordId > 0:
                {
                    var row = await db.GameChallenges.SingleOrDefaultAsync(x => x.Id == finished.Game.RecordId, ct);
                    if (row is null || finished.Result is null) break;
                    row.UserAnswer = finished.Result.Answer;
                    row.Won = finished.Result.Won;
                    row.AnsweredAtUtc = now;
                    break;
                }
                case PermissionAnswered answered:
                    db.Add(new CumRequest { SessionId = sessionId, TimestampUtc = now, Decision = answered.Granted });
                    break;
                case OfferMade made:
                {
                    var o = made.Offer;
                    db.Add(new OfferRecord
                    {
                        Id = o.Id, DefinitionId = o.TemplateId, SessionId = sessionId, Buys = o.Buys, Kind = o.Kind, Amount = o.Amount, Hold = o.Hold,
                        Severity = o.Severity, Points = o.Points, Flavor = o.Flavor, Text = Truncate(o.Text, 1000), Status = OfferStatus.Proposed, CreatedAtUtc = now,
                    });
                    // The ledger entry an acceptance creates refers to the offer row.
                    await db.SaveChangesAsync(ct);
                    break;
                }
                case OfferDecided decided:
                {
                    var row = await db.Offers.SingleOrDefaultAsync(x => x.Id == decided.Offer.Id, ct);
                    if (row is null) break;
                    row.Status = decided.Status;
                    row.DecidedAtUtc = now;
                    await db.SaveChangesAsync(ct);
                    break;
                }
                case LedgerOwed owed when !debug:
                    await LedgerStore.RecordAsync(db, sessionId, owed, now, ct);
                    break;
                case SessionEnded ended:
                {
                    var record = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
                    if (record is null) break;
                    double points;
                    lock (sync) points = State?.Points ?? 0;
                    record.Outcome = ended.Outcome;
                    record.Points = (int)Math.Round(points);
                    record.LastInteractionUtc = now;
                    await db.SaveChangesAsync(ct);
                    if (ended.State is { } finished) await LedgerStore.SettleAsync(db, finished, now, ct);
                    break;
                }
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task RecordContractAsync(GuidedData db, int sessionId, int revision, SessionContract contract, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(contract, Json);
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO aidomme_session_contracts ("UserId", "SessionId", "Revision", "Contract", "AcceptedAtUtc")
            VALUES ({db.UserId}, {sessionId}, {revision}, {json}, {now}) ON CONFLICT DO NOTHING
            """, ct);
    }

    /// <summary>Keeps exactly one OS timer in step with the state's timer generation.</summary>
    private void Arm()
    {
        lock (sync)
        {
            if (disposed) return;
            var s = State;
            long generation = s?.TimerGeneration ?? -1;
            if (s is not { Active: true, Paused: false, TimerDueUtc: DateTime due })
            {
                DisarmLocked();
                return;
            }
            if (generation == armedGeneration && timer is not null) return;
            DisarmLocked();
            armedGeneration = generation;
            var delay = due - Engine.Clock.UtcNow;
            timer = new Timer(Fire, generation, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Disarm() { lock (sync) DisarmLocked(); }

    private void DisarmLocked()
    {
        timer?.Dispose();
        timer = null;
        armedGeneration = -1;
    }

    private void Fire(object? token)
    {
        if (token is not long generation) return;
        // Nobody has been here for a while: the session stops instead of carrying on alone.
        Task Apply() => ApplyAsync(s => Unattended(s) ? Engine.Interrupt(s, lastSeenUtc) : Engine.Timer(s, generation), CancellationToken.None);
        if (TimerDispatcher is { } dispatch) _ = dispatch(Apply);
        else _ = Apply().ContinueWith(x => logger.LogError(x.Exception, "Session timer failed"), TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>A system notice in the chat log.</summary>
    public void Notice(string text) => AddMessage("Manager", text);

    /// <summary>Her words outside a session transition (promises, answers to her questions).</summary>
    public void Say(string text) => AddMessage("Domme", text);

    private void AddMessage(string sender, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (sync)
        {
            messages.Add(new ChatMessage(Guid.NewGuid(), Engine.Clock.UtcNow, sender, text));
            if (messages.Count > MaxMessages) messages.RemoveRange(0, messages.Count - MaxMessages);
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    public static string ModeName(SessionMode mode) => mode switch
    {
        SessionMode.FullGuided => "fully guided",
        SessionMode.OwnContent => "your content, her rules",
        SessionMode.OwnMethods => "your content and methods, her pace",
        SessionMode.FreePlay => "free play, her ending",
        _ => "custom",
    };

    public static string GameName(GameType type) => type switch
    {
        GameType.CoinFlip => "Coin flip", GameType.NumberGuess => "Guess the number", GameType.CardDraw => "High or low",
        GameType.DoubleOrNothing => "Double or nothing", GameType.PickYourPoison => "Pick your poison", GameType.RerollGamble => "Reroll your fate",
        GameType.QuickMath => "Quick math", GameType.RaceToEdge => "Race to the edge", GameType.EdgeEndurance => "Edge endurance",
        GameType.ArousalChallenge => "Don't you dare", GameType.EdgeRush => "Edge rush", GameType.RedLightGreenLight => "Red light, green light",
        GameType.Chicken => "Chicken", GameType.EdgeWager => "Edge wager", GameType.TwoEnvelopes => "Two envelopes",
        _ => type.ToString(),
    };

    public void Dispose()
    {
        lock (sync)
        {
            disposed = true;
            DisarmLocked();
        }
    }
}
