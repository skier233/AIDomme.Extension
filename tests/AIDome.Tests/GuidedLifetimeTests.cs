using System.Reflection;
using AIDome.Persistence;
using AIDome2025.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIDome.Tests;

public sealed class GuidedLifetimeTests
{
    [Fact]
    public void DisposingAnHttpScope_KeepsTimerMessagesUntilTheAccountRuntimeStops()
    {
        var online = DispatchProxy.Create<IOnlineSessionEngine, TimerProxy>();
        var timer = (TimerProxy)(object)online;
        using var owner = new CommandCenterService(NullLogger<CommandCenterService>.Instance, null!, null!, online, null!, null!);
        using var services = new ServiceCollection().AddScoped<ICommandCenterService>(_ => new BorrowedCommandCenter(owner)).BuildServiceProvider();
        using (var first = services.CreateScope()) Assert.Empty(first.ServiceProvider.GetRequiredService<ICommandCenterService>().GetLatestMessages());
        timer.Fire(new() { Text = "Timer advanced." });
        using (var second = services.CreateScope())
            Assert.Equal("Timer advanced.", Assert.Single(second.ServiceProvider.GetRequiredService<ICommandCenterService>().GetLatestMessages()).Content);
        owner.Dispose();
        timer.Fire(new() { Text = "Must not arrive after shutdown." });
        Assert.Single(owner.GetLatestMessages());
    }

    public class TimerProxy : DispatchProxy
    {
        private Action<SessionDirective>? handlers;
        public void Fire(SessionDirective directive) => handlers?.Invoke(directive);
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            switch (method!.Name)
            {
                case "add_StepTimerElapsed": handlers += (Action<SessionDirective>)args![0]!; return null;
                case "remove_StepTimerElapsed": handlers -= (Action<SessionDirective>)args![0]!; return null;
                case "GetCurrentState": return new OnlineSessionState();
                default: throw new NotSupportedException(method.Name);
            }
        }
    }
}
