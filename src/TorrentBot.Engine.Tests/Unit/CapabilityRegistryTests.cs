using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Engine.Capabilities;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class CapabilityRegistryTests
{
    private sealed class NoopHandler : ICapabilityHandler
    {
        public Task<CapabilityResult> ExecuteAsync(
            CapabilityContext context,
            IReadOnlyDictionary<string, object?> parameters,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CapabilityResult(true));
    }

    [Fact]
    public void Register_and_resolve_by_name_and_command()
    {
        var registry = new CapabilityRegistry();
        var metadata = new CapabilityMetadata("test.one", "/one", "desc", "USER", RiskLevel.Safe);
        registry.Register(metadata, new NoopHandler());

        Assert.NotNull(registry.Get("test.one"));
        Assert.Equal("test.one", registry.ResolveCommand("/one"));
    }

    [Fact]
    public void Freeze_prevents_late_registration()
    {
        var registry = new CapabilityRegistry();
        registry.Freeze();

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(
                new CapabilityMetadata("late", null, "desc", "USER", RiskLevel.Safe),
                new NoopHandler()));
    }
}