using System.Text;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class FeatureArtifacts
{
    public static CapabilityResult Binary(string fileName, string contentType, byte[] bytes, string message) =>
        new(true, new Dictionary<string, object?>
        {
            ["toolArtifact"] = new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["contentType"] = contentType,
                ["contentBase64"] = Convert.ToBase64String(bytes)
            }
        }, message);

    public static CapabilityResult TextFile(string fileName, string content, string contentType, string message) =>
        Binary(fileName, contentType, Encoding.UTF8.GetBytes(content), message);
}
