namespace AIDomme.Tracking;

/// <summary>Short, server-owned leases prevent overlapping audio across tabs and devices.
/// Clients stop before the server expiry; a crashed browser cannot retain ownership.</summary>
public sealed class MetronomeLeases
{
    private readonly TimeProvider _time;
    public MetronomeLeases(TimeProvider? time = null) => _time = time ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Dictionary<int, (Guid Client, long Expiry)> _owners = new();
    public bool Acquire(int userId, Guid client)
    {
        if (userId <= 0 || client == Guid.Empty) return false;
        lock (_gate)
        {
            var now = _time.GetTimestamp();
            // Bound memory to currently active browsers, rather than every historic account.
            foreach (var key in _owners.Where(pair => pair.Value.Expiry <= now).Select(pair => pair.Key).ToArray())
                _owners.Remove(key);
            if (_owners.TryGetValue(userId, out var owner) && owner.Client != client) return false;
            _owners[userId] = (client, now + 5 * _time.TimestampFrequency);
            return true;
        }
    }
    public void Release(int userId, Guid client)
    {
        lock (_gate)
            if (_owners.TryGetValue(userId, out var owner) && owner.Client == client)
                _owners.Remove(userId);
    }
}

public sealed record MetronomeLeaseRequest(Guid ClientId);
