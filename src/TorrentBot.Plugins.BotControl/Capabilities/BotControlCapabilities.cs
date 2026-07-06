using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.BotControl.Capabilities;

internal static class BotControlCapabilities
{
    public static readonly CapabilityMetadata DiagMetadata = new(
        "bot.diag",
        "/diag",
        "Show diagnostic status for the media bot.",
        "ALL",
        RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata PluginsMetadata = new(
        "bot.plugins",
        "/plugins",
        "Show hot plugin status.",
        "ALL",
        RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata PluginsReloadMetadata = new(
        "bot.plugins_reload",
        "/plugins_reload",
        "Reload hot plugins.",
        "ADMIN",
        RiskLevel.Admin);

    public static readonly CapabilityMetadata CoordStatusMetadata = new(
        "coord.status",
        "/coord_status",
        "Show coord-input service status.",
        "ALL",
        RiskLevel.Safe,
        IsReadOnly: true);

    public static readonly CapabilityMetadata CoordSubmitMetadata = new(
        "coord.submit",
        "/coord_submit",
        "Submit coordinates to the coord-input service.",
        "USER",
        RiskLevel.ConfirmationRequired);
}