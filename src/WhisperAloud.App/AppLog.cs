using WhisperAloud.Core;

namespace WhisperAloud.App;

internal static class AppLog
{
    private static readonly Lock FileLock = new();

    public static void Write(Exception e) => Append(e.ToString());

    /// <summary>Riga diagnostica: serve a capire dove si ferma una dettatura che non incolla nulla.</summary>
    public static void Info(string message) => Append($"info: {message}");

    private static void Append(string text)
    {
        lock (FileLock)
        {
            File.AppendAllText(AppPaths.AppLog, $"[{DateTimeOffset.Now:O}] {text}{Environment.NewLine}");
        }
    }
}
