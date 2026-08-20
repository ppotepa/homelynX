using System.Security.Cryptography;
using TorrentBot.Plugins.Tools.Chiptune;

namespace TorrentBot.Engine.Tests.Unit;

public sealed class ChiptuneManagedAudioRegressionTests
{
    [Fact]
    public void Controller_volume_zero_produces_real_silence()
    {
        var song = new Song([
            new NoteEvent(0, 960, 69, 110, TrackRole.Lead,
                ControllerChanges: [new ControllerPoint(480, Volume: 0)])
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=A4/4 chip=gbc format=wav");
        var wav = ManagedChipRenderer.Render(VoiceAllocator.Allocate(song, spec));
        var left = Left(wav);
        var half = left.Length / 2;

        Assert.True(Rms(left[..half]) > 500);
        Assert.True(Rms(left[half..]) < 2, "CC7=0 must not be clamped back to an audible tracker volume.");
    }

    [Fact]
    public void Pitch_bend_changes_frequency_without_retriggering_note()
    {
        var song = new Song([
            new NoteEvent(0, 1920, 69, 110, TrackRole.Lead,
                PitchBendRange: 2,
                PitchBends: [new PitchBendPoint(960, 12288)])
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=A4/4 chip=gbc format=wav");
        var wav = ManagedChipRenderer.Render(VoiceAllocator.Allocate(song, spec));
        var left = Left(wav);
        const int rate = 44_100;
        var before = Frequency(left[(int)(rate * .10)..(int)(rate * .40)], rate);
        var after = Frequency(left[(int)(rate * .60)..(int)(rate * .90)], rate);

        Assert.InRange(before, 425, 455);
        Assert.InRange(after, 450, 485);
        Assert.InRange(after / before, 1.045, 1.075);
    }

    [Fact]
    public void Pan_automation_moves_signal_between_stereo_channels()
    {
        var song = new Song([
            new NoteEvent(0, 960, 69, 110, TrackRole.Lead,
                Pan: 0,
                ControllerChanges: [new ControllerPoint(480, Pan: 127)])
        ], TempoMap.Fixed(120));
        var spec = ChiptuneParser.Parse("notes=A4/4 chip=gbc format=wav");
        var wav = ManagedChipRenderer.Render(VoiceAllocator.Allocate(song, spec));
        var (left, right) = Stereo(wav);
        var half = left.Length / 2;

        Assert.True(Rms(left[..half]) > Rms(right[..half]) * 8);
        Assert.True(Rms(right[half..]) > Rms(left[half..]) * 8);
    }

    [Fact]
    public void Managed_sample_patches_are_audibly_distinct()
    {
        var lead = RenderPatch("snes", "sample", "lead");
        var soft = RenderPatch("snes", "sample", "soft_lead");
        var reed = RenderPatch("snes", "sample", "reed");
        var flute = RenderPatch("snes", "sample", "flute");
        var bell = RenderPatch("snes", "sample", "bell");

        Assert.NotEqual(PcmHash(lead), PcmHash(soft));
        Assert.NotEqual(PcmHash(soft), PcmHash(reed));
        Assert.NotEqual(PcmHash(reed), PcmHash(flute));
        Assert.NotEqual(PcmHash(flute), PcmHash(bell));
    }

    [Fact]
    public void Managed_fm_patches_are_audibly_distinct()
    {
        var lead = RenderPatch("genesis", "fm", "lead");
        var epiano = RenderPatch("genesis", "fm", "epiano");
        var brass = RenderPatch("genesis", "fm", "brass");
        var flute = RenderPatch("genesis", "fm", "flute");

        Assert.NotEqual(PcmHash(lead), PcmHash(epiano));
        Assert.NotEqual(PcmHash(epiano), PcmHash(brass));
        Assert.NotEqual(PcmHash(brass), PcmHash(flute));
    }

    private static byte[] RenderPatch(string chip, string voiceClass, string patch)
    {
        var hardware = new HardwareSong(chip, 120, 44_100, TempoMap.Fixed(120).Points,
        [
            new HardwareNote(0, 0, 960, 69, 110, patch, TrackRole.Lead,
                InstrumentId: 0, VoiceClass: voiceClass)
        ], 960);
        return ManagedChipRenderer.Render(hardware);
    }

    private static string PcmHash(byte[] wav)
        => Convert.ToHexString(SHA256.HashData(wav.AsSpan(44).ToArray()));

    private static short[] Left(byte[] wav) => Stereo(wav).Left;

    private static (short[] Left, short[] Right) Stereo(byte[] wav)
    {
        var frames = (wav.Length - 44) / 4;
        var left = new short[frames];
        var right = new short[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = BitConverter.ToInt16(wav, 44 + i * 4);
            right[i] = BitConverter.ToInt16(wav, 46 + i * 4);
        }
        return (left, right);
    }

    private static double Rms(ReadOnlySpan<short> samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var sample in samples) sum += (double)sample * sample;
        return Math.Sqrt(sum / samples.Length);
    }

    private static double Frequency(ReadOnlySpan<short> samples, int rate)
    {
        var crossings = 0;
        for (var i = 1; i < samples.Length; i++)
            if (samples[i - 1] <= 0 && samples[i] > 0) crossings++;
        return crossings / (samples.Length / (double)rate);
    }
}
