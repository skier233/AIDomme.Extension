using System.Collections.Concurrent;
using AIDome2025.Services;
using Cove.Core.Auth;
using Microsoft.Extensions.Logging;

namespace AIDome.Persistence;

public sealed class GuidedRuntimes(GuidedStore store, ICurrentPrincipalAccessor principal, ILoggerFactory logs) : IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<GuidedRuntime>> accounts = new();
    public GuidedRuntime Current => ForUser(principal.Current?.UserId ?? throw new UnauthorizedException("Sign in to use AI Domme."));
    public GuidedRuntime ForUser(int userId) => accounts.GetOrAdd(userId, id => new(() => new GuidedRuntime(store.ForUser(id), logs))).Value;
    public bool TryGet(int userId, out GuidedRuntime? runtime)
    {
        runtime = accounts.TryGetValue(userId, out var lazy) && lazy.IsValueCreated ? lazy.Value : null;
        return runtime is not null;
    }
    public void Dispose()
    {
        foreach (var runtime in accounts.Values.Where(x => x.IsValueCreated)) runtime.Value.Dispose();
        accounts.Clear();
    }
}

public sealed class GuidedRuntime : IDisposable
{
    public BoundGuidedFactory Factory { get; }
    public InteractionManager Interaction { get; }
    public GameEngine Games { get; }
    public OnlineSessionEngine Online { get; }
    public OfflineInstructionEngine Instructions { get; }
    public PunishmentEngine Punishments { get; }
    public CommandCenterService Commands { get; }
    public MediaDirector Media { get; } = new();
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public Guid InstanceId { get; } = Guid.NewGuid();
    public long Revision { get; set; }
    public int PolicyRevision { get; set; }
    public HashSet<Guid> Operations { get; } = [];
    private readonly SemaphoreSlim persistence = new(1, 1);
    private readonly ILogger logger;
    private bool disposed;
    public async Task SaveAsync(CancellationToken ct)
    {
        await persistence.WaitAsync(ct);
        try { await GuidedRecovery.SaveAsync(this, ct); }
        finally { persistence.Release(); }
    }
    private void TimerAdvanced(SessionDirective directive)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await Gate.WaitAsync();
                try { if (!disposed) await SaveAsync(default); }
                finally { Gate.Release(); }
            }
            catch (Exception ex) { logger.LogError(ex, "Could not checkpoint the guided timer"); Online.Pause(); }
        });
    }

    public GuidedRuntime(BoundGuidedFactory factory, ILoggerFactory logs)
    {
        Factory = factory;
        logger = logs.CreateLogger<GuidedRuntime>();
        Games = new(factory, logs.CreateLogger<GameEngine>());
        Online = new(factory, logs.CreateLogger<OnlineSessionEngine>(), Games);
        Interaction = new(factory, () => Online.HasActiveSession ? Online.GetCurrentState().SessionId : null);
        Instructions = new(factory, logs.CreateLogger<OfflineInstructionEngine>());
        Punishments = new(factory, logs.CreateLogger<PunishmentEngine>());
        Commands = new(logs.CreateLogger<CommandCenterService>(), Interaction, factory, Online, Instructions, Games);
        Commands.StartAsync(default).GetAwaiter().GetResult();
        GuidedRecovery.LoadAsync(this, default).GetAwaiter().GetResult();
        Online.StepTimerElapsed += TimerAdvanced;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Online.StepTimerElapsed -= TimerAdvanced;
        Online.Pause();
        // Preserve the final frozen countdown before releasing this runtime.
        try { SaveAsync(default).GetAwaiter().GetResult(); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not save final paused companion state"); }
        Online.EndSession();
        Commands.Dispose();
    }
}
