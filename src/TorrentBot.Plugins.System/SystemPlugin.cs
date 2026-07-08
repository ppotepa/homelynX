using TorrentBot.Contracts.Plugins;
using TorrentBot.Plugins.System.Capabilities;

namespace TorrentBot.Plugins.System;

public sealed class SystemPlugin : IPlugin
{
    public string Name => "system";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(SystemContracts.Health, new HealthCapabilityHandler(), "/health");
        context.RegisterCapability(SystemContracts.Status, new StatusCapabilityHandler(), "/status");
        context.RegisterCapability(SystemContracts.Capabilities, new CapabilitiesListHandler(), "/capabilities");
        context.RegisterCapability(SystemContracts.Ping, new PingCapabilityHandler(), "/ping");
        context.RegisterCapability(SystemContracts.Help, new SystemHelpHandler(), "/help");
        context.RegisterCapability(SystemContracts.LlmStatus, new SystemLlmStatusHandler(), "/llm_status");
        context.RegisterCapability(SystemContracts.DiskUsage, new SystemDiskUsageHandler(), "/disk_usage");
        context.RegisterCapability(SystemContracts.FindLargeFiles, new SystemFindLargeFilesHandler(), "/find_large_files");
        context.RegisterCapability(SystemContracts.LlmPrompt, new SystemLlmPromptDumpHandler(), "/llm_prompt");
    }
}