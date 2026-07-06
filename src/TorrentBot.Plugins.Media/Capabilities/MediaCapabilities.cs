using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Media.Capabilities;

internal static class MediaCapabilities
{
    public static readonly CapabilityMetadata ListMetadata = new(
        Name: "media.list",
        Command: "/media",
        Description: "List known media files in the library",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        LlmUsage: "Use when the user asks what media is available in the library",
        IntentHints: ["media", "library", "files", "movies"],
        IsReadOnly: true);

    public static readonly CapabilityMetadata TtsSayMetadata = new(
        Name: "tts.say",
        Command: "/say",
        Description: "Speak text via TTS service (stub)",
        Permission: "USER",
        Risk: RiskLevel.Safe,
        IsReadOnly: true);
}