using TorrentBot.Contracts.Health;

namespace TorrentBot.Plugins.Tools.Chiptune;

internal sealed class ChiptuneRendererHealth:IHealthContributor
{
    public string Name=>"chiptuneRenderer";
    public async Task<HealthContribution> CheckAsync(CancellationToken ct=default)
    {
        string version;
        try { version=await FurnaceChipRenderer.ProbeAsync(ct); }
        catch when (!ct.IsCancellationRequested) { version="unavailable"; }
        return version=="unavailable"?new("degraded","Furnace renderer unavailable; rebuild homelynx-bot."):new("healthy",version);
    }
}
