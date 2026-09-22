using AIDome.Tracking;
using Xunit;

namespace AIDome.Tests;

public sealed class MetronomeLeaseTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
    }
    [Fact]
    public void ClosedClientExpiresWithoutReleasingAndRenewalExtendsOwnership()
    {
        var time = new Clock();
        var leases = new MetronomeLeases(time);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.True(leases.Acquire(1, first));
        time.Now = 4000;
        Assert.True(leases.Acquire(1, first));
        time.Now = 5001;
        Assert.False(leases.Acquire(1, second));
        time.Now = 9000;
        Assert.True(leases.Acquire(1, second));
        leases.Release(1, first);
        Assert.False(leases.Acquire(1, first));
    }
    [Fact]
    public void OnlyOwnerMayRenewOrReleaseAndAccountsAreIndependent()
    {
        var leases = new MetronomeLeases();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Assert.True(leases.Acquire(1, first));
        Assert.True(leases.Acquire(1, first));
        Assert.False(leases.Acquire(1, second));
        Assert.True(leases.Acquire(2, second));
        leases.Release(1, second);
        Assert.False(leases.Acquire(1, second));
        leases.Release(1, first);
        Assert.True(leases.Acquire(1, second));
        Assert.False(leases.Acquire(0, first));
        Assert.False(leases.Acquire(1, Guid.Empty));
    }
}
