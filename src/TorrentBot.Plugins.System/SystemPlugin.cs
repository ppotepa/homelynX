using TorrentBot.Contracts.Plugins;
using TorrentBot.Plugins.System.Capabilities;
using TorrentBot.Plugins.System.Snapshots;

namespace TorrentBot.Plugins.System;

public sealed class SystemPlugin : IPlugin
{
    public string Name => "system";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(SystemCapabilities.HealthMetadata, new HealthCapabilityHandler());
        context.RegisterCapability(SystemCapabilities.StatusMetadata, new StatusCapabilityHandler());
        context.RegisterCapability(SystemCapabilities.CapabilitiesMetadata, new CapabilitiesListHandler());
        context.RegisterCapability(SystemCapabilities.PingMetadata, new PingCapabilityHandler());
        context.RegisterCapability(SystemCapabilities.HelpMetadata, new SystemHelpHandler());
        context.RegisterCapability(SystemCapabilities.LlmStatusMetadata, new SystemLlmStatusHandler());
        context.RegisterCapability(SystemCapabilities.DiskUsageMetadata, new SystemDiskUsageHandler());
        context.RegisterCapability(SystemCapabilities.FindLargeFilesMetadata, new SystemFindLargeFilesHandler());
        context.RegisterSnapshotSource(new SystemRuntimeSnapshotSource());
    }
}