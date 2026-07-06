using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Context;
using TorrentBot.Contracts.Jobs;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Contracts.Repositories;

namespace TorrentBot.Engine.Tests.Support;

public sealed class TestPlugin : IPlugin
{
    public string Name => "test";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(
            new CapabilityMetadata(
                Name: "test.echo",
                Command: "/echo",
                Description: "Echoes input and optionally creates a job",
                Permission: "USER",
                Risk: RiskLevel.Safe,
                IsReadOnly: true),
            new EchoCapabilityHandler());

        context.RegisterCapability(
            new CapabilityMetadata(
                Name: "test.publish",
                Command: "/publish",
                Description: "Publishes a bus message",
                Permission: "USER",
                Risk: RiskLevel.Safe),
            new PublishCapabilityHandler());

        context.RegisterCapability(
            new CapabilityMetadata(
                Name: "test.context_subscribe",
                Command: "/context_subscribe",
                Description: "Subscribes and publishes via IEngineContext narrow surface",
                Permission: "USER",
                Risk: RiskLevel.Safe),
            new ContextSubscribeCapabilityHandler());

        context.RegisterSnapshotSource(new ItemsSnapshotSource());
    }
}

internal sealed class EchoCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var message = parameters.TryGetValue("message", out var value) ? value?.ToString() : "ok";
        var createJob = parameters.TryGetValue("createJob", out var createJobValue)
                        && createJobValue is true;

        string? jobId = null;
        if (createJob)
        {
            jobId = context.Engine.CreateJob("test.echo", new { message }, new JobOptions());
            context.Engine.UpdateJob(jobId, job => job with { Status = JobStatus.Running, Progress = 0.5 });
        }

        return Task.FromResult(new CapabilityResult(
            Success: true,
            Data: new { echoed = message },
            Message: message,
            JobId: jobId,
            IsDryRun: context.IsDryRun));
    }
}

internal sealed class PublishCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var value = parameters.TryGetValue("value", out var raw) ? raw?.ToString() ?? "event" : "event";
        context.Engine.Publish(new TestBusMessage(value));
        return Task.FromResult(new CapabilityResult(Success: true, Data: new { published = value }));
    }
}

internal sealed class ContextSubscribeCapabilityHandler : ICapabilityHandler
{
    public Task<CapabilityResult> ExecuteAsync(
        CapabilityContext context,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        var payload = parameters.TryGetValue("value", out var raw) ? raw?.ToString() ?? "context-event" : "context-event";
        TestBusMessage? received = null;

        using var _ = context.Engine.Subscribe<TestBusMessage>(message => received = message);
        context.Engine.Publish(new TestBusMessage(payload));

        return Task.FromResult(new CapabilityResult(
            Success: received?.Value == payload,
            Data: new Dictionary<string, object?> { ["received"] = received?.Value, ["expected"] = payload }));
    }
}

internal sealed class ItemsSnapshotSource : ISnapshotSource
{
    public string Name => "test.items";

    public QuerySourceMeta GetManifest() => new(
        Name: Name,
        Description: "Test items snapshot",
        Fields: [new QueryFieldMeta("id", "string"), new QueryFieldMeta("status", "string")]);

    public Task<object> GetSnapshotAsync(CancellationToken ct = default) =>
        Task.FromResult<object>(new List<Dictionary<string, object?>>
        {
            new() { ["id"] = "item-1", ["status"] = "active" },
            new() { ["id"] = "item-2", ["status"] = "inactive" }
        });
}