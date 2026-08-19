namespace TorrentBot.Plugins.Tools.Chiptune;

internal static class MidiImporter
{
    public static Song Import(byte[] bytes, ChiptuneSpec spec) => DryWetMidiImporter.Import(bytes, spec);
}
