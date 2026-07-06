using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Invocation;

namespace TorrentBot.Engine.Tests.Support;

internal static class EngineTestHelper
{
    public static RequestContext CreateRequestContext(
        string traceId = "trace-123",
        string invocationId = "inv-456",
        string userId = "user-789",
        string source = "test") =>
        new(traceId, invocationId, userId, source: source);

    public static UserContext CreateUserContext(string userId = "user-789") =>
        new(userId, ["USER"], "USER");

    public static Invocation CreateInvocation(
        string capabilityName,
        bool isDryRun = false,
        IReadOnlyDictionary<string, object?>? parameters = null,
        RequestContext? requestContext = null,
        UserContext? user = null) =>
        new()
        {
            IsExplicit = true,
            CapabilityName = capabilityName,
            Parameters = parameters,
            RequestContext = requestContext ?? CreateRequestContext(userId: user?.UserId ?? "user-789"),
            User = user ?? CreateUserContext(),
            IsDryRun = isDryRun
        };

    public static async Task<EngineScope> CreateStartedEngineAsync()
    {
        IEngine engine = new EngineHost();
        engine.RegisterPlugin(new TestPlugin());
        await engine.StartAsync();
        return new EngineScope(engine);
    }
}

internal sealed class EngineScope(IEngine engine) : IAsyncDisposable
{
    public IEngine Engine => engine;

    public async ValueTask DisposeAsync() => await engine.StopAsync();
}