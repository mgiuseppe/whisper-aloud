using System.Globalization;

namespace Dettatura.Core;

/// <summary>Una dettatura misurata: tempi in millisecondi dal rilascio della scorciatoia.</summary>
public sealed record LatencySample(
    DateTimeOffset Timestamp,
    string Mode,
    double AudioSeconds,
    long TranscribeMs,
    long CleanMs,
    long PasteMs,
    long TotalMs,
    int Chars);

/// <summary>Registro CSV delle latenze, in %LOCALAPPDATA%\Dettatura\latency.csv.</summary>
public static class LatencyLog
{
    private const string Header = "timestamp,mode,audio_s,transcribe_ms,clean_ms,paste_ms,total_ms,chars";
    private static readonly Lock FileLock = new();

    public static void Append(LatencySample sample, string? path = null)
    {
        path ??= AppPaths.LatencyLog;
        var line = string.Join(',',
            sample.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            sample.Mode,
            sample.AudioSeconds.ToString("0.0", CultureInfo.InvariantCulture),
            sample.TranscribeMs,
            sample.CleanMs,
            sample.PasteMs,
            sample.TotalMs,
            sample.Chars);

        lock (FileLock)
        {
            if (!File.Exists(path)) File.WriteAllText(path, Header + Environment.NewLine);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
