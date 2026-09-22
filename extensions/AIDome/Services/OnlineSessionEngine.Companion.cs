using System.Text.Json;
using AIDome.Data;
using Microsoft.EntityFrameworkCore;

namespace AIDome.Services;

public sealed partial class OnlineSessionEngine
{
    public SessionPolicy Policy { get; private set; } = new();
    private bool _paused;
    private DateTime _pausedAtUtc;
    private long _timerGeneration;
    private bool _pausedTimer;
    public void ObserveEarlierEdge() { lock (_lock) { if (HasActiveSession) _edgesThisSession++; } }
    public async Task PreparePolicyAsync(SessionParameters parameters, CancellationToken ct)
    {
        parameters.Policy.Validate();
        if (parameters.OnlyGamesMode && (!parameters.Policy.GamesEnabled || parameters.Policy.Guidance == "Permission"))
            throw new ArgumentException("Games-only mode requires scheduled games.");
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var methods = await db.StimMethods.ToListAsync(ct);
        if (parameters.Policy.AllowedMethodIds.Any(id => methods.All(x => x.Id != id))) throw new ArgumentException("An allowed method does not exist.");
        var session = new SessionRecord { AllowToys = parameters.AllowToys, AllowVibrator = parameters.AllowVibrator,
            AllowFleshlight = parameters.AllowFleshlight, AllowAutoblow = parameters.AllowAutoblow, AllowProstate = parameters.AllowProstate };
        var candidates = FilterMethodsForSession(methods, session).Where(x => parameters.Policy.AllowedMethodIds.Length == 0 || parameters.Policy.AllowedMethodIds.Contains(x.Id));
        if (parameters.Policy.Guidance == "Simple") candidates = candidates.Where(x => x.ToyCategory == ToyCategory.None && x.TortLevel <= 2 && x.Closeness >= 3);
        if (parameters.Policy.Guidance != "Permission" && !candidates.Any()) throw new ArgumentException("No available methods match the accepted settings.");
        await SetPolicyAsync(parameters.Policy, false, ct);
    }
    private SessionDirective PolicyDirective(SessionDirective directive)
    {
        if (Policy.Pacing != "Free" || directive.Method is null) return directive;
        var text = _activeGameChallengeId is not null ? directive.Text
            : directive.Phase == SessionPhase.Finishing ? "You have permission to orgasm. Use your chosen method at your own pace."
            : $"Use {directive.Method.Name} at your own pace. " + (directive.Kind == InstructionKind.EdgeCount
                ? $"Reach the edge {directive.DurationOrCount} time(s). Hold for {directive.HoldSeconds} seconds when instructed."
                : $"Continue for {directive.DurationOrCount} seconds. Do not orgasm without permission.");
        return new SessionDirective { Kind = directive.Kind, Method = directive.Method, Speed = 0, Tenseness = directive.Tenseness,
            DurationOrCount = directive.DurationOrCount, HoldSeconds = directive.HoldSeconds, Text = text, Phase = directive.Phase };
    }

    private void ArmTimer(int seconds)
    {
        var generation = ++_timerGeneration;
        _stepTimer = new Timer(OnStepTimerElapsed, generation, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
    }

    private List<StimMethod> PolicyMethods(List<StimMethod> methods)
    {
        if (Policy.AllowedMethodIds.Length > 0) methods = methods.Where(x => Policy.AllowedMethodIds.Contains(x.Id)).ToList();
        if (Policy.Guidance == "Simple") methods = methods.Where(x => x.ToyCategory == ToyCategory.None && x.TortLevel <= 2 && x.Closeness >= 3).ToList();
        return methods;
    }

    public async Task SetPolicyAsync(SessionPolicy policy, bool changing, CancellationToken ct)
    {
        policy.Validate();
        if (changing && !Policy.CanChangeDuringSession) throw new InvalidOperationException("Settings were locked at the start of this session.");
        if (changing && policy.CanChangeDuringSession != Policy.CanChangeDuringSession) throw new InvalidOperationException("The accepted change policy cannot be edited during a session.");
        List<StimMethod>? methods = null;
        if (changing)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            methods = FilterMethodsForSession(await db.StimMethods.ToListAsync(ct), _session!);
            // Validate the requested method set without mutating the active policy.
            var candidates = methods.Where(x => policy.AllowedMethodIds.Length == 0 || policy.AllowedMethodIds.Contains(x.Id));
            if (policy.Guidance == "Simple") candidates = candidates.Where(x => x.ToyCategory == ToyCategory.None && x.TortLevel <= 2 && x.Closeness >= 3);
            if (policy.Guidance != "Permission" && !candidates.Any()) throw new ArgumentException("No available methods match these settings.");
        }
        lock (_lock)
        {
            Policy = policy with { AllowedMethodIds = policy.AllowedMethodIds.Distinct().ToArray() };
            if (!changing) return;
            var wasPaused = _paused;
            CancelStepTimer();
            // Keep challenge progress in the database; mode changes never fabricate a loss.
            _activeGameChallengeId = null; _activeGameType = null; _activeGameAnswerReady = false;
            _pendingGameConsequence = false; _pendingLubeDirective = null; _suspendedDirective = null;
            _suspendedRemainingSeconds = 0; _holdingAtEdge = false;
            _gamesOnlyMode = _gamesOnlyMode && Policy.GamesEnabled;
            _edgesThisStep = 0; _currentDirective = null; _finishOffered = false;
            _chainMethod = null; _chainStepsRemaining = 0;
            _eligibleMethods = PolicyMethods(methods!);
            _paused = false;
            NextDirectiveLocked();
            if (wasPaused) Pause();
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_paused || !HasActiveSession) return;
            _pausedAtUtc = DateTime.UtcNow;
            _pausedTimer = _stepTimer is not null;
            var remaining = _pausedTimer ? Math.Max(1, (int)Math.Ceiling(_stepDurationSeconds - (_pausedAtUtc - _stepStartUtc).TotalSeconds)) : 0;
            CancelStepTimer();
            _stepStartUtc = _pausedAtUtc;
            _stepDurationSeconds = remaining;
            _paused = true;
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (!_paused || !HasActiveSession) return;
            _sessionStartUtc += DateTime.UtcNow - _pausedAtUtc;
            _stepStartUtc = DateTime.UtcNow;
            _paused = false;
            if (_pausedTimer) ArmTimer(Math.Max(1, _stepDurationSeconds));
        }
    }

    public void GrantPermission()
    {
        lock (_lock)
        {
            CancelStepTimer(); _finishOffered = true; _stepNumber++;
            _currentDirective = new SessionDirective { Kind = InstructionKind.FreeText, Phase = SessionPhase.Finishing, Text = "Permission granted. You may orgasm. Record it when it happens." };
        }
    }
}
