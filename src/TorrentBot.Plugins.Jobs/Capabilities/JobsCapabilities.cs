using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Jobs.Capabilities;

internal static class JobsCapabilities
{
    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "jobs.list",
        Command: "/jobs",
        Description: "List tracked engine jobs",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks about background jobs",
        IsReadOnly: true);

    public static readonly CapabilityMetadata CancelMetadata = new(
        Name: "jobs.cancel",
        Command: "/job_cancel",
        Description: "Cancel a tracked engine job",
        Permission: "USER",
        Risk: RiskLevel.Destructive,
        LlmUsage: "Use to cancel a running or queued job by id");
}