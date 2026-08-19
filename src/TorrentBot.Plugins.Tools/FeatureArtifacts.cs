using System.Text;
using TorrentBot.Contracts.Capabilities;

namespace TorrentBot.Plugins.Tools;

internal static class FeatureArtifacts
{
    public static CapabilityResult Binary(string fileName, string contentType, byte[] bytes, string message, IReadOnlyList<Dictionary<string,object?>>? actions=null) =>
        new(true, BuildData(fileName,contentType,bytes,actions), message);

    private static Dictionary<string,object?> BuildData(string fileName,string contentType,byte[] bytes,IReadOnlyList<Dictionary<string,object?>>? actions)
    {
        var data = new Dictionary<string, object?>
        {
            ["toolArtifact"] = new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["contentType"] = contentType,
                ["contentBase64"] = Convert.ToBase64String(bytes)
            }
        };
        if(actions is { Count:>0 })data["toolActions"]=actions;
        return data;
    }

    public static CapabilityResult TextFile(string fileName, string content, string contentType, string message) =>
        Binary(fileName, contentType, Encoding.UTF8.GetBytes(content), message);
}
