using TorrentBot.Contracts.Plugins;

namespace TorrentBot.Plugins.Query;

public sealed class QueryPlugin : IPlugin
{
    public string Name => "query";
    public string Version => "1.0.0";

    public void Register(IPluginRegistrationContext context)
    {
        context.RegisterCapability(QueryContracts.Execute, new QueryExecuteHandler());
    }
}