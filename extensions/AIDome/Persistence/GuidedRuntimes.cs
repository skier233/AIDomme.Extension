using System.Collections.Concurrent;
using AIDome.Services;
using Cove.Core.Auth;
using Cove.Plugins;
using Microsoft.Extensions.Logging;

namespace AIDome.Persistence;

/// <summary>
/// One live guided runtime per Cove account. Runtimes load asynchronously on the account's first
/// request (restoring any saved checkpoint) and stay resident until the extension stops.
/// </summary>
public sealed class GuidedRuntimes(IExtensionServiceScopeFactory scopes, ICurrentPrincipalAccessor principal, ILoggerFactory logs)
    : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<int, Lazy<Task<GuidedRuntime>>> accounts = new();

    private int SignedInUser => principal.Current?.UserId is int user and > 0 ? user
        : throw new UnauthorizedException("Sign in to use AI Domme.");

    /// <summary>The signed-in account's runtime. Requests load it first through <see cref="EnsureCurrentAsync"/>.</summary>
    public GuidedRuntime Current => TryGet(SignedInUser, out var runtime) ? runtime!
        : throw new InvalidOperationException("AI Domme is still loading this account. Try again.");

    public Task<GuidedRuntime> EnsureCurrentAsync(CancellationToken ct) => ForUserAsync(SignedInUser, ct);

    public async Task<GuidedRuntime> ForUserAsync(int userId, CancellationToken ct = default)
    {
        if (userId <= 0) throw new UnauthorizedException("Sign in to use AI Domme.");
        var lazy = accounts.GetOrAdd(userId, id => new(() => GuidedRuntime.CreateAsync(new GuidedDataFactory(scopes, id), logs)));
        try { return await lazy.Value.WaitAsync(ct); }
        catch when (lazy.Value.IsFaulted)
        {
            // A failed load (for example the database was briefly unavailable) is retried next time.
            accounts.TryRemove(new(userId, lazy));
            throw;
        }
    }

    public bool TryGet(int userId, out GuidedRuntime? runtime)
    {
        runtime = accounts.TryGetValue(userId, out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully
            ? lazy.Value.Result : null;
        return runtime is not null;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var lazy in accounts.Values.Where(x => x.IsValueCreated))
            if (lazy.Value.IsCompletedSuccessfully) await lazy.Value.Result.DisposeAsync();
        accounts.Clear();
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

public sealed class GuidedRuntime : IAsyncDisposable
{
    public GuidedDataFactory Factory { get; }
    public InteractionManager Interaction { get; }
    public GameEngine Games { get; }
    public OnlineSessionEngine Online { get; }
    public OfflineInstructionEngine Instructions { get; }
    public CommandCenterService Commands { get; }
    public MediaDirector Media { get; } = new();
    /// <summary>Serializes every change to this account's live session: requests, tracker delivery and timers.</summary>
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public Guid InstanceId { get; } = Guid.NewGuid();
    public long Revision { get; set; }
    public int PolicyRevision { get; set; }
    public HashSet<Guid> Operations { get; } = [];
    internal string? LastSavedPayload { get; set; }
    internal DateTime LastSavedAtUtc { get; set; }
    private readonly SemaphoreSlim persistence = new(1, 1);
    private readonly ILogger logger;
    private bool loaded;
    private bool disposed;

    private GuidedRuntime(GuidedDataFactory factory, ILoggerFactory logs)
    {
        Factory = factory;
        logger = logs.CreateLogger<GuidedRuntime>();
        Games = new(factory, logs.CreateLogger<GameEngine>());
        Online = new(factory, logs.CreateLogger<OnlineSessionEngine>(), Games) { TimerDispatcher = DispatchTimerAsync };
        Interaction = new(factory, () => Online.HasActiveSession ? Online.GetCurrentState().SessionId : null);
        Instructions = new(factory, logs.CreateLogger<OfflineInstructionEngine>());
        Commands = new(logs.CreateLogger<CommandCenterService>(), Interaction, factory, Online, Instructions, Games);
    }

    public static async Task<GuidedRuntime> CreateAsync(GuidedDataFactory factory, ILoggerFactory logs)
    {
        var runtime = new GuidedRuntime(factory, logs);
        try
        {
            await runtime.Commands.StartAsync(default);
            await GuidedRecovery.LoadAsync(runtime, default);
            runtime.loaded = true;
            return runtime;
        }
        catch
        {
            await runtime.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Persists the recovery checkpoint. Unforced saves come from status polls and only refresh the
    /// running timer's remaining time occasionally; every state change is saved with force.
    /// </summary>
    public async Task SaveAsync(CancellationToken ct, bool force = true)
    {
        await persistence.WaitAsync(ct);
        try { await GuidedRecovery.SaveAsync(this, force, ct); }
        finally { persistence.Release(); }
    }

    /// <summary>Runs an engine timer transition under the account gate, then checkpoints it.</summary>
    private async Task DispatchTimerAsync(Action advance)
    {
        try
        {
            await Gate.WaitAsync();
            try
            {
                if (disposed) return;
                advance();
                await SaveAsync(default);
            }
            finally { Gate.Release(); }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not apply or checkpoint the guided timer");
            Online.Pause();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        Online.Pause();
        if (loaded)
        {
            // Preserve the final frozen countdown before releasing this runtime.
            try { await SaveAsync(default); }
            catch (Exception ex) { logger.LogWarning(ex, "Could not save final paused companion state"); }
        }
        Online.EndSession();
        Commands.Dispose();
    }
}
