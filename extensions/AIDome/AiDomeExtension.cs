using System.Text.Json;
using System.Text.Json.Serialization;

using AIDome.Data;
using AIDome.Services;
using AIDome.Tracking;
using AIDome.Persistence;
using Cove.Data;

using Cove.Plugins;
using Cove.Sdk;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace AIDome;

/// <summary>
/// AI Domme — Cove-backed tracking and guided experiences, with account-bound runtimes,
/// shared definition/calibration defaults and personal overrides. SQLite is read-only
/// migration input; live session persistence uses Cove's PostgreSQL database.
/// </summary>
public sealed class AiDomeExtension : FullExtensionBase
{
    private const string SettingsStoreKey = "settings";

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() },
    };

    private IServiceProvider? _services;
    private TtsOptions? _ttsOptions;
    private GuidedRuntimes? _runtimes;

    public override string Id => "cove.community.aidome";
    public override string Name => "AI Domme";
    public override string Version => "0.6.0";
    public override string Description => "A rule-driven domme for guided edging sessions on your Cove library, with an app-wide edge tracker, orgasm permission, games and persistent commitments.";
    public override string Author => "Cove Community";
    public override string Url => "https://github.com/yourcove/AIDome.Extension";

    public override IReadOnlyList<string> Categories =>
    [
        ExtensionCategories.Tools,
        ExtensionCategories.Automation,
    ];

    public override string MinCoveVersion => "1.4.2-dev.44";

    public override void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        var dbPath = Path.Combine(context.DataDirectory, "aidome.db");
        services.AddSingleton(sp =>
        {
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoveContext>();
            var sourceFile = Path.Combine(context.DataDirectory, "aidomme-legacy-configuration.json");
            var originalPath = Environment.GetEnvironmentVariable("AIDOMME_LEGACY_CONFIGURATION_PATH");
            if (string.IsNullOrWhiteSpace(originalPath) && File.Exists(sourceFile))
                originalPath = JsonSerializer.Deserialize<LegacyDefinitionSource>(File.ReadAllText(sourceFile), JsonOpts)?.Path;
            return new GuidedStore(db.Database.GetConnectionString() ?? throw new InvalidOperationException("Cove PostgreSQL is unavailable."), dbPath, originalPath);
        });
        services.AddSingleton<GuidedRuntimes>();
        services.AddScoped<GuidedTrackerBridge>();
        services.AddScoped<GuidedConfiguration>();
        services.AddScoped<CommitmentStore>();
        services.AddScoped<TrackerService>();
        services.AddSingleton<MetronomeLeases>();
        services.AddSingleton<NarrationLeases>();
        services.AddSingleton<MediaLeases>();
        services.AddScoped<IDbContextFactory<CommandCenterDbContext>>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Factory);
        services.AddScoped<IInteractionManager>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Interaction);
        services.AddScoped<IOnlineSessionEngine>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Online);
        services.AddScoped<IInstructionEngine>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Instructions);
        services.AddScoped<IPunishmentEngine>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Punishments);
        services.AddScoped<IGameEngine>(sp => sp.GetRequiredService<GuidedRuntimes>().Current.Games);
        services.AddScoped<ICommandCenterService>(sp => new BorrowedCommandCenter(sp.GetRequiredService<GuidedRuntimes>().Current.Commands));

        // TTS: editable settings (loaded from the store in InitializeAsync) + per-call HttpClient.
        services.TryAddSingleton<TtsOptions>();
        services.AddHttpClient();
        services.AddSingleton<ITtsService, OrpheusTtsService>();
    }

    public override async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        _services = services;

        // Host migrations create the Cove-backed tables. The legacy file is read-only input;
        // only definitions and calibration are imported, and only once.
        await services.GetRequiredService<GuidedStore>().ImportAsync(ct);
        _runtimes = services.GetRequiredService<GuidedRuntimes>();

        // Load persisted TTS settings into the shared singleton.
        _ttsOptions = services.GetRequiredService<TtsOptions>();
        var settings = await LoadSettingsAsync(ct);
        settings.ApplyTo(_ttsOptions);

    }

    public override async Task ShutdownAsync(CancellationToken ct = default)
    {
        _runtimes?.Dispose();
    }

    public override UIManifest GetUIManifest()
        => ManifestBuilder()
            .AddPage("aidome", "AI Domme", "CommandCenterPage", icon: "flame", showInNav: true, navOrder: 90)
            .AddPage("aidome-tuning", "AI Domme Tuning", "TuningLabPage", icon: "sliders-horizontal", showInNav: false)
            .AddSettingsPanel(new UISettingsPanel("aidome-settings", "AI Domme", Id, "AiDomeSettingsPanel", 40))
            .AddSlot("app-floating-ui", "TrackerCompanion")
            .AddKeyboardAction("tracker-edge", "Record edge", ["Alt+E"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("tracker-orgasm", "Record orgasm", ["Alt+O"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("tracker-toggle", "Show / hide tracker", ["Alt+D"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("metronome-toggle", "Start / pause metronome", ["Alt+M"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-complete", "Complete ready / finish step", ["Alt+C"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-permission", "Request orgasm permission", ["Alt+B"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-stop-request", "Request a rest", ["Alt+S"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-pause", "Pause / resume guided session", ["Alt+P"], [new("global"), new("overlay")], group: "AI Domme")
            .Build();

    public override void ConfigureModel(ModelBuilder modelBuilder) => TrackerSchema.Configure(modelBuilder);
    protected override void DefineMigrations()
    {
        Migration("001_tracker", TrackerSchema.Sql);
        Migration("002_guided", GuidedSchema.CreateSql() + GuidedStore.ImportSql);
        Migration("003_companion", GuidedRecovery.Sql);
        Migration("004_commitments", CommitmentStore.Sql);
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ext/aidome").WithTags("AIDome").AllowWithoutCovePermission();
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var path = invocation.HttpContext.Request.Path.Value ?? "";
            if (invocation.HttpContext.RequestServices.GetRequiredService<Cove.Core.Auth.ICurrentPrincipalAccessor>().Current?.UserId is null)
                throw new Cove.Core.Auth.UnauthorizedException("Sign in to use AI Domme.");
            // Tracker delivery owns the runtime gate. Other guided requests serialize by account.
            if (path.StartsWith("/api/ext/aidome/tracker/", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("/api/ext/aidome/session/action/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/tts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/settings", StringComparison.OrdinalIgnoreCase))
                return await next(invocation);
            var runtime = invocation.HttpContext.RequestServices.GetRequiredService<GuidedRuntimes>().Current;
            await runtime.Gate.WaitAsync(invocation.HttpContext.RequestAborted);
            try
            {
                await invocation.HttpContext.RequestServices.GetRequiredService<GuidedTrackerBridge>().DrainUnderGateAsync(runtime, invocation.HttpContext.RequestAborted);
                return await next(invocation);
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
            finally { runtime.Gate.Release(); }
        });
        var tracker = group.MapGroup("/tracker").AllowWithoutCovePermission();
        tracker.MapPost("/metronome/lease", (MetronomeLeaseRequest request, MetronomeLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
            request.ClientId == Guid.Empty ? Results.BadRequest(new { message = "A metronome client ID is required." })
                : Results.Json(new { granted = leases.Acquire(principal.Current!.UserId!.Value, request.ClientId) }));
        tracker.MapPost("/metronome/release", (MetronomeLeaseRequest request, MetronomeLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
        {
            leases.Release(principal.Current!.UserId!.Value, request.ClientId);
            return Results.NoContent();
        });
        tracker.MapGet("/state", async (TrackerService service, CancellationToken ct) => JsonResult(await service.SnapshotAsync(ct)));
        tracker.MapPost("/initialize", async (TrackerService service, CancellationToken ct) =>
            await TrackerResultAsync(async () => await service.SettingsAsync(null, false, ct)));
        tracker.MapPut("/settings", async (TrackerSettingsRequest request, TrackerService service, CancellationToken ct) =>
            await TrackerResultAsync(async () => await service.SettingsAsync(request, false, ct)));
        tracker.MapPost("/setting", async (TrackerService service, CancellationToken ct) =>
            await TrackerResultAsync(async () => await service.SettingsAsync(null, true, ct)));
        tracker.MapPost("/events", async (RecordTrackerEvent request, TrackerService service, CancellationToken ct) =>
            await TrackerResultAsync(async () => await service.RecordAsync(request, ct)));
        tracker.MapPost("/events/{id:guid}/undo", async (Guid id, TrackerService service, CancellationToken ct) =>
            await TrackerResultAsync(async () => await service.UndoAsync(id, ct)));

        group.MapGet("/configuration", async (GuidedConfiguration config, CancellationToken ct) => JsonResult(await config.SnapshotAsync(ct)));
        group.MapPut("/configuration/methods/{id:int}", async (int id, StimMethod body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SaveAsync(id, body, ct)));
        group.MapDelete("/configuration/methods/{id:int}/override", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.ResetAsync<StimMethod>(id, ct)));
        group.MapPut("/configuration/enders/{id:int}", async (int id, PunishmentEnder body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SaveAsync(id, body, ct)));
        group.MapDelete("/configuration/enders/{id:int}/override", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.ResetAsync<PunishmentEnder>(id, ct)));
        group.MapPost("/configuration/optimize", async (GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.OptimizeAsync(ct)));

        group.MapGet("/commitments", async (CommitmentStore store, CancellationToken ct) => JsonResult(await store.GetAsync(ct)));
        group.MapPost("/commitments/mutate", async (CommitmentMutation request, CommitmentStore store, CancellationToken ct) =>
            JsonResult(await store.MutateAsync(request, ct)));

        // ── Live state ──
        group.MapGet("/state", async (ICommandCenterService cc, CancellationToken ct) =>
            JsonResult(await BuildSnapshotAsync(cc, ct)));

        group.MapGet("/setup", async (ICommandCenterService cc, CancellationToken ct) =>
        {
            var defaults = await cc.GetResolvedDefaultsAsync(ct);
            var punishments = await cc.GetActivePunishmentSummariesAsync(ct);
            var suspicion = await cc.CheckPreviousSessionAsync(ct);
            return JsonResult(new AiDomeSetupSnapshot(defaults, punishments, suspicion, cc.GetLatestMessages()));
        });

        group.MapGet("/history", async (ICommandCenterService cc, int? count, CancellationToken ct) =>
            JsonResult(await cc.GetSessionHistoryAsync(count ?? 10, ct)));

        // ── Session lifecycle ──
        group.MapPost("/session/start", async (SessionParameters parameters, ICommandCenterService cc, CancellationToken ct) =>
        {
            parameters.Policy.Validate();
            if (_runtimes!.Current.Online.HasActiveSession) return Results.Conflict(new { message = "End the current session before starting another." });
            await cc.StartSessionAsync(parameters, ct);
            _runtimes!.Current.PolicyRevision = 1;
            _runtimes.Current.Operations.Clear();
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        group.MapPost("/session/start-punishment", async (SessionParameters parameters, ICommandCenterService cc, CancellationToken ct) =>
        {
            parameters.Policy.Validate();
            if (_runtimes!.Current.Online.HasActiveSession) return Results.Conflict(new { message = "End the current session before starting another." });
            await cc.StartPunishmentSessionAsync(parameters, ct);
            _runtimes!.Current.PolicyRevision = 1;
            _runtimes.Current.Operations.Clear();
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        group.MapPost("/session/end", async (ICommandCenterService cc, CancellationToken ct) =>
        {
            if (PauseOnly(_runtimes!.Current)) return PauseOnlyConflict();
            await cc.EndSessionAsync(ct);
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        group.MapPut("/session/policy", async (SessionPolicyRequest request, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            var state = runtime.Online.GetCurrentState();
            if (!runtime.Online.HasActiveSession || state.SessionId != request.SessionId || runtime.PolicyRevision != request.Revision)
                return Results.Conflict(new { message = "Session settings changed elsewhere. Refresh before saving." });
            await runtime.Online.SetPolicyAsync(request.Policy, true, ct);
            runtime.PolicyRevision++;
            runtime.Commands.CompanionNotice("Session settings updated. The previous prompt/challenge was replaced without treating it as a failure.");
            runtime.Commands.CompanionDirective();
            return JsonResult(await BuildSnapshotAsync(runtime.Commands, ct));
        });
        group.MapPost("/session/control", async (SessionControlRequest request, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            if (request.EventId == Guid.Empty) return Results.BadRequest(new { message = "A command event ID is required." });
            await using var commandDb = await runtime.Factory.CreateDbContextAsync(ct);
            var receipt = await commandDb.Database.SqlQuery<GuidedRecovery.ControlRow>($"""
                SELECT "Payload", "Status" FROM aidomme_guided_controls WHERE "OwnerId" = {commandDb.OwnerId} AND "EventId" = {request.EventId}
                """).SingleOrDefaultAsync(ct);
            if (receipt is not null)
            {
                if (JsonSerializer.Deserialize<SessionControlRequest>(receipt.Payload, GuidedStore.Json) != request)
                    return Results.BadRequest(new { message = "The command ID belongs to different inputs." });
                if (receipt.Status == "Applied" || runtime.Operations.Contains(request.EventId)) return JsonResult(await BuildSnapshotAsync(runtime.Commands, ct));
                runtime.Online.EndSession();
                runtime.Commands.CompanionNotice("Guidance stopped after an interrupted command. Its uncertain effects will not be repeated. Review the current state before starting another session.");
                await runtime.SaveAsync(ct);
                return Results.Conflict(new { message = "This command was interrupted; it was not replayed. Review state and dismiss the request." });
            }
            var state = runtime.Online.GetCurrentState();
            if (!runtime.Online.HasActiveSession || request.SessionId != state.SessionId)
                return Results.Conflict(new { message = "The session changed. Refresh before acting." });
            if (request.Action is not ("pause" or "resume" or "end") && (state.IsPaused || request.StepNumber != state.StepNumber))
                return Results.Conflict(new { message = "The step changed or is paused. Review the current instruction." });
            if (request.Action is not ("pause" or "resume" or "end" or "game-answer" or "done" or "beg-to-cum" or "beg-to-stop"))
                return Results.BadRequest(new { message = "Unknown session control." });
            if (request.Action == "end" && PauseOnly(runtime)) return PauseOnlyConflict();
            if (request.Action == "done" && !state.IsLubePause && state.CurrentDirective?.Kind != AIDome.Data.InstructionKind.Finish)
                return Results.Conflict(new { message = "This step advances through its timer, edge target or challenge result." });
            if (request.Action == "game-answer" && request.Answer is null) return Results.BadRequest(new { message = "An answer is required." });
            await runtime.SaveAsync(ct);
            var payload = JsonSerializer.Serialize(request, GuidedStore.Json);
            await commandDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_guided_controls ("OwnerId", "EventId", "Payload", "Status") VALUES ({commandDb.OwnerId}, {request.EventId}, {payload}, 'Pending')
                """, ct);
            switch (request.Action)
            {
                case "pause": runtime.Online.Pause(); runtime.Commands.CompanionNotice("Session paused."); break;
                case "resume": runtime.Online.Resume(); runtime.Commands.CompanionNotice("Session resumed."); break;
                case "end": await runtime.Commands.EndSessionAsync(ct); break;
                case "game-answer":
                    if (request.Answer is null) return Results.BadRequest(new { message = "An answer is required." });
                    await runtime.Commands.SubmitGameAnswerAsync(request.Answer.Value, ct); break;
                case "done": case "beg-to-cum": case "beg-to-stop": await runtime.Commands.TriggerActionAsync(request.Action, ct); break;
                default: return Results.BadRequest(new { message = "Unknown session control." });
            }
            runtime.Operations.Add(request.EventId);
            return JsonResult(await BuildSnapshotAsync(runtime.Commands, ct));
        });
        group.MapPost("/companion/audio/lease", (MetronomeLeaseRequest request, NarrationLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
            Results.Json(new { granted = leases.Acquire(principal.Current!.UserId!.Value, request.ClientId) }));
        group.MapPost("/companion/media/lease", (MetronomeLeaseRequest request, MediaLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
            Results.Json(new { granted = leases.Acquire(principal.Current!.UserId!.Value, request.ClientId) }));
        group.MapPost("/companion/media/release", (MetronomeLeaseRequest request, MediaLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
        {
            leases.Release(principal.Current!.UserId!.Value, request.ClientId); return Results.NoContent();
        });
        group.MapPost("/companion/audio/release", (MetronomeLeaseRequest request, NarrationLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
        {
            leases.Release(principal.Current!.UserId!.Value, request.ClientId); return Results.NoContent();
        });
        group.MapPost("/companion/audio/claim", async (NarrationClaimRequest request, NarrationLeases leases, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            var state = runtime.Online.GetCurrentState();
            if (!runtime.Online.HasActiveSession || state.IsPaused || !runtime.Commands.GetLatestMessages().Any(x => x.Id == request.MessageId && x.Sender == "Domme")
                || !leases.Acquire(runtime.Factory.UserId, request.ClientId)) return Results.Json(new { granted = false });
            await using var db = await runtime.Factory.CreateDbContextAsync(ct);
            var now = DateTime.UtcNow;
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_narration_receipts ("OwnerId", "MessageId", "ClaimedAtUtc")
                VALUES ({db.OwnerId}, {request.MessageId}, {now}) ON CONFLICT DO NOTHING
                """, ct);
            return Results.Json(new { granted = claimed == 1 });
        });

        // ── In-session actions ──
        group.MapPost("/session/action/{actionId}", async (string actionId, GuidedActionRequest? body, GuidedRuntimes runtimes, TrackerService tracking, CancellationToken ct) =>
        {
            var runtime = runtimes.Current;
            actionId = actionId.ToLowerInvariant();
            if (actionId is "edged" or "orgasm")
            {
                var snapshot = await tracking.SnapshotAsync(ct);
                var request = new RecordTrackerEvent(body?.EventId ?? Guid.NewGuid(), body?.SettingId ?? snapshot.Preferences.SettingId,
                    actionId == "edged" ? "edge" : "orgasm", body?.OccurredAtUtc ?? DateTime.UtcNow, body?.Source ?? "button",
                    body?.MediaKind, body?.MediaId, body?.PositionSeconds);
                try { await tracking.RecordAsync(request, ct); }
                catch (TrackerValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
            }
            else
            {
                await runtime.Gate.WaitAsync(ct);
                try
                {
                    await runtimes.Current.Commands.TriggerActionAsync(actionId, ct);
                }
                finally { runtime.Gate.Release(); }
            }
            return JsonResult(await BuildSnapshotAsync(runtime.Commands, ct));
        });

        group.MapPost("/session/game/answer", async (AiDomeGameAnswerRequest body, ICommandCenterService cc, CancellationToken ct) =>
        {
            await cc.SubmitGameAnswerAsync(body.Answer, ct);
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        group.MapPost("/session/infraction", async (AiDomeInfractionRequest body, ICommandCenterService cc, CancellationToken ct) =>
        {
            if (!Enum.TryParse<InfractionType>(body.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new { message = $"Unknown infraction type '{body.Type}'." });
            await cc.ReportInfractionAsync(type, body.Note, ct);
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        group.MapPost("/session/cum-request", async (AiDomeCumRequest body, ICommandCenterService cc, CancellationToken ct) =>
        {
            await cc.RecordCumRequestAsync(body.Decision, ct);
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        // ── Confession (abandoned previous session) ──
        group.MapPost("/confession", async (AiDomeConfessionRequest body, ICommandCenterService cc, CancellationToken ct) =>
        {
            await cc.HandleConfessionAsync(body.Confessed, ct);
            return JsonResult(await BuildSnapshotAsync(cc, ct));
        });

        // ── Offline homework generation ──
        group.MapPost("/homework", async (SessionParameters parameters, ICommandCenterService cc, CancellationToken ct) =>
            JsonResult(await cc.GenerateDebugHomeworkAsync(parameters, ct)));

        group.MapPost("/punishment-homework", async (SessionParameters parameters, ICommandCenterService cc, CancellationToken ct) =>
            JsonResult(await cc.GenerateDebugPunishmentHomeworkAsync(parameters, ct)));

        // ── Settings ──
        group.MapGet("/settings", async (CancellationToken ct) => JsonResult(await LoadSettingsAsync(ct)));

        group.MapPut("/settings", async (AiDomeSettings body, CancellationToken ct) =>
        {
            var normalized = new AiDomeSettings(
                (body.TtsBaseUrl ?? "").Trim(),
                string.IsNullOrWhiteSpace(body.TtsVoice) ? "mia" : body.TtsVoice.Trim(),
                body.TtsTimeoutSeconds <= 0 ? 30 : body.TtsTimeoutSeconds);

            await SaveSettingsAsync(normalized, ct);
            if (_ttsOptions is not null)
                normalized.ApplyTo(_ttsOptions);
            return JsonResult(normalized);
        });

        // ── TTS proxy ──
        group.MapPost("/tts", async (AiDomeTtsRequest body, ITtsService tts, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.Text))
                return Results.BadRequest(new { message = "Text is required." });

            var audio = await tts.SynthesizeAsync(body.Text, ct);
            return audio is null ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable) : Results.File(audio, "audio/wav");
        });
    }

    private async Task<AiDomeStateSnapshot> BuildSnapshotAsync(ICommandCenterService cc, CancellationToken ct)
    {
        var runtime = _runtimes!.Current;
        await runtime.SaveAsync(ct);
        var status = await cc.GetSessionStatusAsync(ct);
        var online = cc.GetOnlineState();
        var active = status.IsActive && online is not null;
        DateTime? stepEndsAt = online is { RemainingSeconds: >= 0, IsPaused: false }
            ? DateTime.UtcNow.AddSeconds(online.RemainingSeconds)
            : null;

        var mediaCue = runtime.Online.Policy.Media == "Engine" && online?.IsPaused != true
            ? runtime.Media.Advance(online?.SessionId, active) : null;

        return new AiDomeStateSnapshot(
            UserId: runtime.Factory.UserId,
            InstanceId: runtime.InstanceId,
            Revision: runtime.Revision,
            PolicyRevision: runtime.PolicyRevision,
            Policy: runtime.Online.Policy,
            SessionActive: active,
            LastInteractionUtc: status.LastInteractionUtc,
            Points: cc.GetCurrentPoints(),
            Online: online,
            StepEndsAtUtc: stepEndsAt,
            MediaCue: mediaCue,
            Messages: cc.GetLatestMessages(),
            Actions: cc.GetCurrentActions());
    }

    private async Task<AiDomeSettings> LoadSettingsAsync(CancellationToken ct)
    {
        var payload = await Store.GetAsync(SettingsStoreKey, ct);
        if (string.IsNullOrWhiteSpace(payload))
            return new AiDomeSettings("", "mia", 30);

        return JsonSerializer.Deserialize<AiDomeSettings>(payload, JsonOpts) ?? new AiDomeSettings("", "mia", 30);
    }

    private Task SaveSettingsAsync(AiDomeSettings settings, CancellationToken ct)
        => Store.SetAsync(SettingsStoreKey, JsonSerializer.Serialize(settings, JsonOpts), ct);

    private static IResult JsonResult<T>(T value) => Results.Json(value, JsonOpts);

    // Outside debug sessions the user can pause but not end; the domme decides when it is over.
    private static bool PauseOnly(GuidedRuntime runtime)
        => runtime.Online.HasActiveSession && !runtime.Online.GetCurrentState().IsDebugSession;
    private static IResult PauseOnlyConflict()
        => Results.Conflict(new { message = "This session can be paused, not ended. The domme decides when it is over." });

    private static async Task<IResult> TrackerResultAsync<T>(Func<Task<T>> action)
    {
        try { return JsonResult(await action()); }
        catch (TrackerValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
    }
}

public sealed record LegacyDefinitionSource(string Path);
