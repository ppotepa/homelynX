namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class InstrumentCatalog
{
    private static readonly IReadOnlyDictionary<string, int> PatchIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["lead"] = 0, ["soft_lead"] = 1, ["pluck"] = 2, ["bass"] = 3,
        ["strings"] = 4, ["brass"] = 5, ["reed"] = 6, ["pad"] = 7,
        ["bell"] = 8, ["organ"] = 9, ["epiano"] = 10, ["flute"] = 11,
        ["kick"] = 16, ["snare"] = 17, ["hat"] = 18, ["open_hat"] = 19,
        ["tom"] = 20, ["crash"] = 21, ["ride"] = 22, ["drums"] = 23
    };

    public static int Id(string patch, ChipVoiceClass voiceClass)
    {
        var baseId = PatchIds.GetValueOrDefault(patch, PatchIds["lead"]);
        // Genesis FM and PSG need distinct instrument definitions even when
        // they use the same semantic patch. The ID remains compact and is
        // never interpreted as a hardware channel number.
        var offset = voiceClass == ChipVoiceClass.Psg ? 32 : voiceClass == ChipVoiceClass.Noise ? 48 : 0;
        return baseId + offset;
    }

    public static bool IsKnown(string patch) => PatchIds.ContainsKey(patch);
}
