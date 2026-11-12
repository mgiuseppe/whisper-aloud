using WhisperAloud.Core.Transcription;

namespace WhisperAloud.Core.Tests;

public sealed class RecordingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WhisperAloudTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Save_ThenRead_GivesBackTheSameAudio()
    {
        var samples = Enumerable.Range(0, WhisperTranscriber.SampleRate)
            .Select(i => 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / WhisperTranscriber.SampleRate)).ToArray();

        var path = Recordings.Save(_dir, samples, new DateTime(2026, 11, 12, 9, 30, 5));
        var read = AudioFile.ReadForWhisper(path);

        Assert.Equal("2026-11-12_09-30-05.wav", Path.GetFileName(path));
        Assert.Equal(samples.Length, read.Length);
        Assert.All(Enumerable.Range(0, samples.Length), i => Assert.Equal(samples[i], read[i], 0.001f));
    }

    [Fact]
    public void Save_SameSecond_DoesNotOverwrite()
    {
        var when = new DateTime(2026, 11, 12, 9, 30, 5);
        var first = Recordings.Save(_dir, new float[160], when);
        var second = Recordings.Save(_dir, new float[160], when);

        Assert.NotEqual(first, second);
        Assert.True(File.Exists(first));
        Assert.EndsWith("_2.wav", second);
    }

    [Fact]
    public void DeleteOlderThan_KeepsRecentFilesAndOtherTypes()
    {
        var now = new DateTime(2026, 11, 12, 12, 0, 0);
        var old = Recordings.Save(_dir, new float[160], now.AddDays(-10));
        var recent = Recordings.Save(_dir, new float[160], now.AddDays(-1));
        File.SetLastWriteTime(old, now.AddDays(-10));
        File.SetLastWriteTime(recent, now.AddDays(-1));
        var other = Path.Combine(_dir, "notes.txt");
        File.WriteAllText(other, "x");
        File.SetLastWriteTime(other, now.AddDays(-30));

        Assert.Equal(0, Recordings.DeleteOlderThan(_dir, 0, now)); // 0 = mai
        Assert.Equal(1, Recordings.DeleteOlderThan(_dir, 7, now));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
