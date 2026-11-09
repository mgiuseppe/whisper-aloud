using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WhisperAloud.Core.Transcription;

/// <summary>Legge un file audio e lo converte nel formato di Whisper: mono, 16 kHz, float.</summary>
public static class AudioFile
{
    public static float[] ReadForWhisper(string path)
    {
        using var reader = new AudioFileReader(path);
        ISampleProvider provider = reader;
        if (provider.WaveFormat.Channels > 1) provider = provider.ToMono();
        if (provider.WaveFormat.SampleRate != WhisperTranscriber.SampleRate)
        {
            provider = new WdlResamplingSampleProvider(provider, WhisperTranscriber.SampleRate);
        }

        var samples = new List<float>();
        var buffer = new float[WhisperTranscriber.SampleRate];
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }
        return [.. samples];
    }
}
