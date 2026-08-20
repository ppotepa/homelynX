namespace TorrentBot.Plugins.Tools.Chiptune;

internal enum ChipVoiceClass
{
    Pulse,
    Wave,
    Triangle,
    Noise,
    Dpcm,
    Fm,
    Psg,
    Sample,
    Wavetable,
    Pokey,
    Tia,
    Beeper
}

internal sealed record ChipVoiceProfile(
    int Index,
    ChipVoiceClass Class,
    bool Melodic,
    bool Percussion,
    int Priority = 0);

internal sealed class ChipProfile
{
    private ChipProfile(string id, IReadOnlyList<ChipVoiceProfile> voices)
    {
        Id = id;
        Voices = voices;
    }

    public string Id { get; }
    public IReadOnlyList<ChipVoiceProfile> Voices { get; }

    public ChipVoiceProfile Voice(int index) =>
        Voices.FirstOrDefault(x => x.Index == index) ??
        throw new InvalidOperationException($"Chip '{Id}' has no voice {index}.");

    public IReadOnlyList<int> Candidates(NoteEvent note)
    {
        var melodic = Voices.Where(x => x.Melodic).OrderByDescending(x => x.Priority).Select(x => x.Index).ToArray();
        var percussion = Voices.Where(x => x.Percussion).OrderByDescending(x => x.Priority).Select(x => x.Index).ToArray();
        if (note.Role == TrackRole.Drums)
            return Id == "nes" ? (note.Pitch is >= 35 and <= 40 ? [4, 3] : [3]) : percussion;

        // Program identity is stronger than the coarse Lead/Harmony role for
        // obvious GM bass families. Multiple bass tracks can therefore still
        // reach triangle/wave/FM voices even if only one was labeled Bass by
        // the source-part classifier.
        var bassLike = note.Role == TrackRole.Bass ||
                       (note.SourceTrack >= 0 && note.Program is >= 32 and <= 39);

        IEnumerable<int> preferred;
        if (bassLike)
        {
            preferred = Voices
                .Where(x => x.Class is ChipVoiceClass.Triangle or ChipVoiceClass.Wave or ChipVoiceClass.Fm or ChipVoiceClass.Wavetable or ChipVoiceClass.Sample)
                .OrderByDescending(x => x.Class is ChipVoiceClass.Triangle or ChipVoiceClass.Wave ? 120 : x.Priority)
                .Select(x => x.Index);
        }
        else if (note.Role == TrackRole.Arp)
        {
            preferred = Voices
                .Where(x => x.Class is ChipVoiceClass.Psg or ChipVoiceClass.Pulse or ChipVoiceClass.Wavetable or ChipVoiceClass.Sample)
                .OrderByDescending(x => x.Class is ChipVoiceClass.Psg ? 120 : x.Priority)
                .Select(x => x.Index);
        }
        else
        {
            preferred = Voices
                .Where(x => x.Melodic && x.Class is not (ChipVoiceClass.Triangle or ChipVoiceClass.Wave))
                .OrderByDescending(x => x.Priority)
                .Select(x => x.Index);
        }

        return preferred.Concat(melodic).Distinct().ToArray();
    }

    public static ChipProfile For(string chip) => chip.ToLowerInvariant() switch
    {
        "gb" or "gbc" or "gameboy" => new("gbc", [
            new(0, ChipVoiceClass.Pulse, true, false, 100), new(1, ChipVoiceClass.Pulse, true, false, 95),
            new(2, ChipVoiceClass.Wave, true, false, 90), new(3, ChipVoiceClass.Noise, false, true, 100)]),
        "nes" => new("nes", [
            new(0, ChipVoiceClass.Pulse, true, false, 100), new(1, ChipVoiceClass.Pulse, true, false, 95),
            new(2, ChipVoiceClass.Triangle, true, false, 110), new(3, ChipVoiceClass.Noise, false, true, 100),
            new(4, ChipVoiceClass.Dpcm, false, true, 105)]),
        "sms" => new("sms", [
            new(0, ChipVoiceClass.Pulse, true, false, 100), new(1, ChipVoiceClass.Pulse, true, false, 95),
            new(2, ChipVoiceClass.Pulse, true, false, 90), new(3, ChipVoiceClass.Noise, false, true, 100)]),
        "snes" => new("snes", Enumerable.Range(0, 8).Select(x => new ChipVoiceProfile(x, ChipVoiceClass.Sample, true, true, 100 - x)).ToArray()),
        "pce" => new("pce", Enumerable.Range(0, 6).Select(x => new ChipVoiceProfile(x, ChipVoiceClass.Wavetable, true, x >= 4, 100 - x)).ToArray()),
        "genesis" => new("genesis", [
            new(0, ChipVoiceClass.Fm, true, false, 110), new(1, ChipVoiceClass.Fm, true, false, 105),
            new(2, ChipVoiceClass.Fm, true, false, 100), new(3, ChipVoiceClass.Fm, true, false, 95),
            new(4, ChipVoiceClass.Fm, true, false, 90), new(5, ChipVoiceClass.Fm, true, false, 85),
            new(6, ChipVoiceClass.Psg, true, false, 70), new(7, ChipVoiceClass.Psg, true, false, 65),
            new(8, ChipVoiceClass.Psg, true, false, 60), new(9, ChipVoiceClass.Noise, false, true, 100)]),
        "c64_6581" or "c64_8580" => new(chip, Enumerable.Range(0, 3).Select(x => new ChipVoiceProfile(x, ChipVoiceClass.Pulse, true, true, 100 - x)).ToArray()),
        "pokey" => new("pokey", Enumerable.Range(0, 4).Select(x => new ChipVoiceProfile(x, ChipVoiceClass.Pokey, true, true, 100 - x)).ToArray()),
        "atari2600" => new("atari2600", [new(0, ChipVoiceClass.Tia, true, true, 100), new(1, ChipVoiceClass.Tia, true, true, 90)]),
        "zx_spectrum" => new("zx_spectrum", Enumerable.Range(0, 6).Select(x => new ChipVoiceProfile(x, ChipVoiceClass.Beeper, true, x == 5, 100 - x)).ToArray()),
        "pcspeaker" => new("pcspeaker", [new(0, ChipVoiceClass.Beeper, true, true, 100)]),
        _ => throw new FormatException($"Unsupported chip profile '{chip}'.")
    };
}
