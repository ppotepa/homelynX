using TorrentBot.Contracts.Plugins;
using TorrentBot.Plugins.Jobs.Capabilities;
using TorrentBot.Plugins.Jobs.Snapshots;

namespace TorrentBot.Plugins.Jobs;

public sealed class JobsPlugin : IPlugin
{
    public string Name => "jobs";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(JobsCapabilities.ListMetadata, new JobsListHandler());
        context.RegisterCapability(JobsCapabilities.CancelMetadata, new JobsCancelHandler());
        context.RegisterSnapshotSource(new EngineJobsSnapshotSource());
    }
}