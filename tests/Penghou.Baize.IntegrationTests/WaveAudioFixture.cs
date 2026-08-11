using System.Text;

namespace Penghou.Baize.IntegrationTests;

internal static class WaveAudioFixture
{
    private const int SampleRate = 16_000;
    private const short BitsPerSample = 16;
    private const short ChannelCount = 1;

    public static byte[] CreateThreeBeeps()
    {
        var samples = new List<short>();
        AddSilence(samples, 0.25);
        AddTone(samples, 660, 0.50);
        AddSilence(samples, 0.50);
        AddTone(samples, 660, 0.50);
        AddSilence(samples, 0.50);
        AddTone(samples, 660, 0.50);
        AddSilence(samples, 0.25);
        return WriteWave(samples);
    }

    private static void AddTone(
        ICollection<short> samples,
        double frequency,
        double durationSeconds)
    {
        var sampleCount = (int)(SampleRate * durationSeconds);
        var fadeSamples = (int)(SampleRate * 0.01);
        for (var index = 0; index < sampleCount; index++)
        {
            var envelope = Math.Min(
                1d,
                Math.Min(
                    (double)index / fadeSamples,
                    (double)(sampleCount - index - 1) / fadeSamples));
            var value = Math.Sin(2 * Math.PI * frequency * index / SampleRate);
            samples.Add((short)(short.MaxValue * 0.4 * envelope * value));
        }
    }

    private static void AddSilence(
        ICollection<short> samples,
        double durationSeconds)
    {
        var sampleCount = (int)(SampleRate * durationSeconds);
        for (var index = 0; index < sampleCount; index++)
            samples.Add(0);
    }

    private static byte[] WriteWave(IReadOnlyCollection<short> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var dataLength = samples.Count * sizeof(short);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(ChannelCount);
        writer.Write(SampleRate);
        writer.Write(SampleRate * ChannelCount * BitsPerSample / 8);
        writer.Write((short)(ChannelCount * BitsPerSample / 8));
        writer.Write(BitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (var sample in samples)
            writer.Write(sample);

        writer.Flush();
        return stream.ToArray();
    }
}
