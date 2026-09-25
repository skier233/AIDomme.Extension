using System.Text.Json;
using System.Text.Json.Serialization;

using AIDome.Data;
using AIDome.Services;
using AIDome.Tracking;
using AIDome.Persistence;
using AIDome.Sessions;
using AIDome.Sessions.Plan;
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
    public override string Version => "0.15.0";
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
        // Guided storage lives in Cove's own database and context. Each account gets one resident
        // runtime; request-scoped services borrow the signed-in account's runtime.
        services.AddSingleton<GuidedRuntimes>();
        services.AddScoped<GuidedTrackerBridge>();
        services.AddScoped<GuidedConfiguration>();
        services.AddScoped<TrackerService>();
        services.AddSingleton<MetronomeLeases>();
        services.AddSingleton<NarrationLeases>();
        services.AddSingleton<ScreenResolver>();

        // TTS: editable settings (loaded from the store in InitializeAsync) + per-call HttpClient.
        services.TryAddSingleton<TtsOptions>();
        services.AddHttpClient();
        services.AddSingleton<ITtsService, OrpheusTtsService>();
    }

    public override async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        _services = services;
        _runtimes = services.GetRequiredService<GuidedRuntimes>();

        // Load persisted TTS settings into the shared singleton.
        _ttsOptions = services.GetRequiredService<TtsOptions>();
        var settings = await LoadSettingsAsync(ct);
        settings.ApplyTo(_ttsOptions);
    }

    public override async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (_runtimes is not null) await _runtimes.DisposeAsync();
    }

    /// <summary>AI Domme's own tab in Cove's settings (/settings/extensions/aidome).</summary>
    public const string SettingsTab = "extensions/aidome";

    public override UIManifest GetUIManifest()
        => ManifestBuilder()
            .AddPage("aidome", "AI Domme", "CommandCenterPage", icon: "flame", showInNav: true, navOrder: 90)
            // Everything the user sets once lives in Cove's settings, under its own tab.
            .AddSettingsTab(SettingsTab, "AI Domme", order: 110, icon: "flame",
                description: "Her persona, how your sessions end, session length, her methods and deals, and narration.",
                searchKeywords: ["ai domme", "domme", "persona", "denial", "ruin", "orgasm", "methods", "offers", "deals", "tts"],
                aliases: ["aidome", "aidomme"])
            .AddSettingsPanel(new UISettingsPanel("aidome-persona", "Her persona", Id, "HerPersonaPanel", 10, TargetTab: SettingsTab))
            .AddSettingsPanel(new UISettingsPanel("aidome-endings", "How your sessions end", Id, "EndingsPanel", 20, TargetTab: SettingsTab))
            .AddSettingsPanel(new UISettingsPanel("aidome-lengths", "Session length", Id, "LengthsPanel", 30, TargetTab: SettingsTab))
            .AddSettingsPanel(new UISettingsPanel("aidome-segments", "What she plans", Id, "SegmentsPanel", 40, TargetTab: SettingsTab))
            .AddSettingsPanel(new UISettingsPanel("aidome-tuning", "Tuning lab", Id, "TuningLabLinkPanel", 50, TargetTab: SettingsTab))
            .AddSettingsPanel(new UISettingsPanel("aidome-settings", "Narration voice", Id, "AiDomeSettingsPanel", 60, TargetTab: SettingsTab))
            // The tuning lab is a full page; the settings tab links to it.
            .AddPage("aidome-tuning", "AI Domme tuning lab", "TuningLabPage", icon: "sliders-horizontal", showInNav: false)
            .AddSlot("app-floating-ui", "TrackerCompanion")
            .AddKeyboardAction("tracker-edge", "Record edge", ["Alt+E"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("tracker-orgasm", "Record orgasm", ["Alt+O"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("tracker-toggle", "Show / hide tracker", ["Alt+D"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("metronome-toggle", "Start / pause metronome", ["Alt+M"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-complete", "Ready / acknowledge", ["Alt+C"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-permission", "Ask permission to cum", ["Alt+B"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-stop-request", "Beg for a rest", ["Alt+S"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("session-pause", "Pause / resume session", ["Alt+P"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("offer-accept", "Accept her offer", ["Alt+Y"], [new("global"), new("overlay")], group: "AI Domme")
            .AddKeyboardAction("offer-decline", "Decline her offer", ["Alt+N"], [new("global"), new("overlay")], group: "AI Domme")
            .Build();

    public override void ConfigureModel(ModelBuilder modelBuilder)
    {
        TrackerSchema.Configure(modelBuilder);
        GuidedSchema.Configure(modelBuilder);
    }

    protected override void DefineMigrations()
    {
        Migration("001_tracker", TrackerSchema.Sql);
        Migration("002_guided", GuidedMigrations.Superseded);
        Migration("003_companion", GuidedMigrations.Superseded);
        Migration("004_commitments", GuidedMigrations.Superseded);
        Migration("005_guided_v2", GuidedMigrations.Load("005_guided_v2"));
        Migration("006_session_contracts", GuidedMigrations.Load("006_session_contracts"));
        Migration("007_ledger", GuidedMigrations.Load("007_ledger"));
        Migration("008_voice", GuidedMigrations.Load("008_voice"));
        Migration("009_pace", GuidedMigrations.Load("009_pace"));
        Migration("010_offer_values", GuidedMigrations.Load("010_offer_values"));
        Migration("011_removed_definitions", GuidedMigrations.Load("011_removed_definitions"));
    }

    public override void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/ext/aidome").WithTags("AIDome").AllowWithoutCovePermission();
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var path = invocation.HttpContext.Request.Path.Value ?? "";
            if (invocation.HttpContext.RequestServices.GetRequiredService<Cove.Core.Auth.ICurrentPrincipalAccessor>().Current?.UserId is null)
                throw new Cove.Core.Auth.UnauthorizedException("Sign in to use AI Domme.");
            // Load (or restore) the account's runtime before any handler borrows it.
            var runtime = await invocation.HttpContext.RequestServices.GetRequiredService<GuidedRuntimes>().EnsureCurrentAsync(invocation.HttpContext.RequestAborted);
            // Tracker delivery owns the runtime gate. Other guided requests serialize by account.
            if (path.StartsWith("/api/ext/aidome/tracker/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/tts", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/settings", StringComparison.OrdinalIgnoreCase))
                return await next(invocation);
            await runtime.Gate.WaitAsync(invocation.HttpContext.RequestAborted);
            try
            {
                var ct = invocation.HttpContext.RequestAborted;
                await invocation.HttpContext.RequestServices.GetRequiredService<GuidedTrackerBridge>().DrainUnderGateAsync(runtime, ct);
                // A session nobody was watching stops as of the last visit; then facts are reconciled
                // (breaches become questions) before anything else is answered.
                await runtime.Session.InterruptIfUnattendedAsync(ct);
                runtime.Session.Seen();
                await runtime.Session.Ledger.ReconcileAsync(ct);
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
        // New definitions of the user's own, and taking methods or endings out of the library (and back).
        group.MapPost("/configuration/methods", async (StimMethod body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.CreateAsync(body, ct)));
        group.MapPost("/configuration/enders", async (PunishmentEnder body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.CreateAsync(body, ct)));
        group.MapPost("/configuration/offers", async (OfferDefinition body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.CreateAsync(body, ct)));
        group.MapPost("/configuration/methods/{id:int}/remove", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SetRemovedAsync<StimMethod>(id, true, ct)));
        group.MapPost("/configuration/methods/{id:int}/restore", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SetRemovedAsync<StimMethod>(id, false, ct)));
        group.MapPost("/configuration/enders/{id:int}/remove", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SetRemovedAsync<PunishmentEnder>(id, true, ct)));
        group.MapPost("/configuration/enders/{id:int}/restore", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SetRemovedAsync<PunishmentEnder>(id, false, ct)));
        group.MapPost("/configuration/optimize", async (GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.OptimizeAsync(ct)));
        // The tuning lab: the next side-by-side matchup, recording a choice, taking the last one back.
        group.MapGet("/configuration/lab", async (string? mode, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.LabAsync(mode ?? TuningLab.Stim, ct)));
        group.MapPost("/configuration/optimize-deals", async (GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.OptimizeDealsAsync(ct)));
        group.MapGet("/configuration/lab/suggestions", async (GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SuggestionsAsync(ct)));
        group.MapPost("/configuration/comparisons", async (ComparisonRequest body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.CompareAsync(body, ct)));
        group.MapDelete("/configuration/comparisons/last", async (string? mode, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.UndoAsync(mode ?? TuningLab.Stim, ct)));

        group.MapPut("/configuration/offers/{id:int}", async (int id, OfferDefinition body, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.SaveAsync(id, body, ct)));
        group.MapDelete("/configuration/offers/{id:int}/override", async (int id, GuidedConfiguration config, CancellationToken ct) =>
            await TrackerResultAsync(async () => await config.ResetAsync<OfferDefinition>(id, ct)));

        // ── Promises, the ledger and her questions ──
        group.MapGet("/ledger", async (CancellationToken ct) => JsonResult(await _runtimes!.Current.Session.Ledger.ViewAsync(ct)));
        group.MapPost("/commitments/propose", async (CommitmentProposal request, CancellationToken ct) =>
        {
            if (request?.Terms is null) return Results.BadRequest(new { message = "Terms are required." });
            await _runtimes!.Current.Session.Ledger.ProposeAsync(request.Terms, ct);
            return JsonResult(await _runtimes.Current.Session.Ledger.ViewAsync(ct));
        });
        group.MapPost("/commitments/{id:guid}/accept", async (Guid id, CancellationToken ct) =>
        {
            await _runtimes!.Current.Session.Ledger.DecideAsync(id, accept: true, ct);
            return JsonResult(await _runtimes.Current.Session.Ledger.ViewAsync(ct));
        });
        group.MapPost("/commitments/{id:guid}/decline", async (Guid id, CancellationToken ct) =>
        {
            await _runtimes!.Current.Session.Ledger.DecideAsync(id, accept: false, ct);
            return JsonResult(await _runtimes.Current.Session.Ledger.ViewAsync(ct));
        });
        group.MapPost("/followups/{id:guid}/answer", async (Guid id, FollowUpAnswerRequest request, CancellationToken ct) =>
        {
            await _runtimes!.Current.Session.Ledger.AnswerAsync(id, request?.Answer ?? "", ct);
            return JsonResult(await BuildSnapshotAsync(ct));
        });

        // ── Live state ──
        // Polls only refresh the saved countdown occasionally; state changes are saved when they happen.
        // The page reports what is on screen with each poll (media=video:12:345.6), so she can talk about it.
        group.MapGet("/state", async (string? media, HttpContext http, ILoggerFactory logs, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            if (ScreenResolver.Parse(media) is var (kind, id, position) && http.RequestServices.GetService<ScreenResolver>() is { } screens)
            {
                try { runtime.Session.See(await screens.ResolveAsync(http.RequestServices.GetRequiredService<DbContext>(), kind, id, position, DateTime.UtcNow, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logs.CreateLogger("AIDome").LogDebug(ex, "Could not read what is on screen"); }
            }
            return JsonResult(await BuildSnapshotAsync(ct, persist: false));
        });

        // ── Her persona and how she plans ──
        group.MapGet("/domme-settings", async (CancellationToken ct) =>
        {
            await using var db = _runtimes!.Current.Factory.Open();
            return JsonResult(new
            {
                Settings = await RecallStore.SettingsAsync(db, ct),
                Segments = SegmentCatalogue.Default.Types.Where(x => x.Planned).Select(x => new { x.Id, x.Name, x.Goal, x.Tags }),
            });
        });
        group.MapPut("/domme-settings", async (DommeSettings body, CancellationToken ct) =>
        {
            if (body is null) return Results.BadRequest(new { message = "Settings are required." });
            var settings = body.Validate(SegmentCatalogue.Default);
            await using var db = _runtimes!.Current.Factory.Open();
            await RecallStore.SaveSettingsAsync(db, settings, DateTime.UtcNow, ct);
            return JsonResult(settings);
        });

        group.MapGet("/setup", async (CancellationToken ct) =>
        {
            var session = _runtimes!.Current.Session;
            var defaults = await session.ResolvedDefaultsAsync(ct);
            await using var db = _runtimes.Current.Factory.Open();
            var lengths = (await RecallStore.SettingsAsync(db, ct)).Lengths;
            var minutes = Enumerable.Range(1, 10).Select(x => (int)Math.Round(lengths.Minutes(x))).ToList();
            return JsonResult(new AiDomeSetupSnapshot(defaults, await session.Ledger.ViewAsync(ct), ModePresets, session.Messages(), minutes));
        });

        group.MapGet("/history", async (int? count, CancellationToken ct) =>
            JsonResult(await _runtimes!.Current.Session.HistoryAsync(count ?? 10, ct)));

        // ── Session lifecycle ──
        group.MapPost("/session/start", async (StartSessionRequest request, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            if (request.Contract is null) return Results.BadRequest(new { message = "A session contract is required." });
            if (runtime.Session.Active) return Results.Conflict(new { message = "The current session isn't over yet." });
            await runtime.Session.StartAsync(request.Contract, ct);
            runtime.Operations.Clear();
            return JsonResult(await BuildSnapshotAsync(ct));
        });

        group.MapPut("/session/contract", async (SessionContractRequest request, CancellationToken ct) =>
        {
            if (request.Contract is null) return Results.BadRequest(new { message = "A session contract is required." });
            await _runtimes!.Current.Session.ChangeContractAsync(request.SessionId, request.Revision, request.Contract, ct);
            return JsonResult(await BuildSnapshotAsync(ct));
        });

        group.MapPost("/session/control", async (SessionControlRequest request, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            if (request.EventId == Guid.Empty) return Results.BadRequest(new { message = "A command event ID is required." });
            await using var commandDb = runtime.Factory.Open();
            var receipt = await commandDb.Database.SqlQuery<GuidedRecovery.ControlRow>($"""
                SELECT "Payload", "Status" FROM aidomme_session_controls WHERE "UserId" = {commandDb.UserId} AND "EventId" = {request.EventId}
                """).SingleOrDefaultAsync(ct);
            if (receipt is not null)
            {
                if (JsonSerializer.Deserialize<SessionControlRequest>(receipt.Payload, GuidedRecovery.Json) != request)
                    return Results.BadRequest(new { message = "The command ID belongs to different inputs." });
                if (receipt.Status == "Applied" || runtime.Operations.Contains(request.EventId)) return JsonResult(await BuildSnapshotAsync(ct));
                runtime.Session.Abandon("Guidance stopped after an interrupted command. Its uncertain effects will not be repeated. Review the current state before starting another session.");
                await runtime.SaveAsync(ct);
                return Results.Conflict(new { message = "This command was interrupted; it was not replayed. Review state and dismiss the request." });
            }
            var session = runtime.Session.View();
            if (session is null || request.SessionId != session.SessionId)
                return Results.Conflict(new { message = "The session changed. Refresh before acting." });
            var action = request.Action switch { "beg-to-cum" => "request-permission", "beg-to-stop" => "request-rest", var other => other };
            if (action is not ("pause" or "resume" or "end" or "game-answer" or "done" or "request-permission" or "request-rest" or "offer-accept" or "offer-decline"))
                return Results.BadRequest(new { message = "Unknown session control." });
            // Step-specific controls must still refer to the instruction the user saw.
            if (action is "done" or "game-answer" && (session.IsPaused || request.StepNumber != session.StepNumber))
                return Results.Conflict(new { message = "The step changed or is paused. Review the current instruction." });
            if (action == "end" && !session.IsDebugSession) return PauseOnlyConflict();
            if (action == "game-answer" && request.Answer is null) return Results.BadRequest(new { message = "An answer is required." });
            await runtime.SaveAsync(ct);
            var payload = JsonSerializer.Serialize(request, GuidedRecovery.Json);
            await commandDb.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_session_controls ("UserId", "EventId", "Payload", "Status") VALUES ({commandDb.UserId}, {request.EventId}, {payload}, 'Pending')
                """, ct);
            await runtime.Session.ControlAsync(action, request.Answer, ct);
            runtime.Operations.Add(request.EventId);
            return JsonResult(await BuildSnapshotAsync(ct));
        });

        group.MapPost("/session/infraction", async (AiDomeInfractionRequest body, CancellationToken ct) =>
        {
            if (!Enum.TryParse<InfractionType>(body.Type, ignoreCase: true, out var type))
                return Results.BadRequest(new { message = $"Unknown infraction type '{body.Type}'." });
            await _runtimes!.Current.Session.ReportInfractionAsync(type, body.Note, ct);
            return JsonResult(await BuildSnapshotAsync(ct));
        });

        // ── Offline homework ──
        group.MapPost("/homework", async (HomeworkRequest request, CancellationToken ct) =>
        {
            if (request.Contract is null) return Results.BadRequest(new { message = "A session contract is required." });
            return JsonResult(await _runtimes!.Current.Session.GenerateHomeworkAsync(request.Contract, request.Punishment, ct));
        });

        group.MapPost("/companion/audio/lease", (MetronomeLeaseRequest request, NarrationLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
            Results.Json(new { granted = leases.Acquire(principal.Current!.UserId!.Value, request.ClientId) }));
        group.MapPost("/companion/audio/release", (MetronomeLeaseRequest request, NarrationLeases leases, Cove.Core.Auth.ICurrentPrincipalAccessor principal) =>
        {
            leases.Release(principal.Current!.UserId!.Value, request.ClientId); return Results.NoContent();
        });
        group.MapPost("/companion/audio/claim", async (NarrationClaimRequest request, NarrationLeases leases, CancellationToken ct) =>
        {
            var runtime = _runtimes!.Current;
            if (!runtime.Session.Active || runtime.Session.Paused || !runtime.Session.Messages().Any(x => x.Id == request.MessageId && x.Sender == "Domme")
                || !leases.Acquire(runtime.Factory.UserId, request.ClientId)) return Results.Json(new { granted = false });
            await using var db = runtime.Factory.Open();
            var now = DateTime.UtcNow;
            var claimed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO aidomme_narration_claims ("UserId", "MessageId", "ClaimedAtUtc")
                VALUES ({db.UserId}, {request.MessageId}, {now}) ON CONFLICT DO NOTHING
                """, ct);
            return Results.Json(new { granted = claimed == 1 });
        });

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

    private async Task<AiDomeStateSnapshot> BuildSnapshotAsync(CancellationToken ct, bool persist = true)
    {
        var runtime = _runtimes!.Current;
        await runtime.SaveAsync(ct, force: persist);
        var session = runtime.Session.View();
        // She only picks what to watch when the contract gives her the content.
        var mediaCue = session is { IsPaused: false, Contract.Content: Authority.Domme }
            ? runtime.Media.Advance(session.SessionId, true) : runtime.Media.Advance(null, false);
        return new AiDomeStateSnapshot(runtime.Factory.UserId, runtime.InstanceId, runtime.Revision, session is not null,
            session, runtime.Session.StepEndsAtUtc(), mediaCue, runtime.Session.Messages(), await runtime.Session.Ledger.OpenFollowUpsAsync(ct));
    }

    private static readonly IReadOnlyList<ModePresetView> ModePresets =
    [
        new(SessionMode.FullGuided, "Fully guided", "She picks what you watch, how you touch yourself, the pace, your edges, games and how it ends.",
            SessionContract.Preset(SessionMode.FullGuided)),
        new(SessionMode.OwnContent, "Your content, her rules", "You browse your own content. She decides everything else.",
            SessionContract.Preset(SessionMode.OwnContent)),
        new(SessionMode.OwnMethods, "Your hands, her pace", "You choose the content and how you touch yourself. She sets the pace, edges, holds and the ending.",
            SessionContract.Preset(SessionMode.OwnMethods)),
        new(SessionMode.FreePlay, "Free play, her ending", "Do as you like and record your edges. She decides when, and whether, you cum.",
            SessionContract.Preset(SessionMode.FreePlay)),
    ];

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
    private static IResult PauseOnlyConflict()
        => Results.Conflict(new { message = "This session can be paused, not ended. The domme decides when it is over." });

    private static async Task<IResult> TrackerResultAsync<T>(Func<Task<T>> action)
    {
        try { return JsonResult(await action()); }
        catch (TrackerValidationException ex) { return Results.BadRequest(new { message = ex.Message }); }
    }
}

