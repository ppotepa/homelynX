using System.Reflection;
using TorrentBot.Contracts.Capabilities;
using TorrentBot.Contracts.Plugins;
using TorrentBot.Engine.Capabilities;
using TorrentBot.Engine.Capabilities.Attributes;

namespace TorrentBot.Engine.Plugins;

public static class PluginLoader
{
    public static void RegisterPlugin(IPlugin plugin, PluginRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(context);
        plugin.Register(context);
    }

    public static void RegisterAttributedCapabilities(Assembly assembly, PluginRegistrationContext context)
    {
        foreach (var type in assembly.GetTypes())
        {
            var attribute = type.GetCustomAttribute<CapabilityAttribute>();
            if (attribute is null || !typeof(ICapabilityHandler).IsAssignableFrom(type))
            {
                continue;
            }

            var handler = (ICapabilityHandler)Activator.CreateInstance(type)!;
            var metadata = new CapabilityMetadata(
                Name: attribute.Name,
                Command: attribute.Command,
                Description: attribute.Description,
                Permission: attribute.Permission,
                Risk: attribute.Risk,
                LlmUsage: attribute.LlmUsage,
                IntentHints: attribute.IntentHints,
                Preconditions: attribute.Preconditions,
                IsLongRunning: attribute.IsLongRunning,
                IsReadOnly: attribute.IsReadOnly);

            context.RegisterCapability(metadata, handler);
        }
    }
}