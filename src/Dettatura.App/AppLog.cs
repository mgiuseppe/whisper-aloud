using Dettatura.Core;

namespace Dettatura.App;

internal static class AppLog
{
    private static readonly Lock FileLock = new();

    public static void Write(Exception e)
    {
        lock (FileLock)
        {
            File.AppendAllText(AppPaths.AppLog, $"[{DateTimeOffset.Now:O}] {e}{Environment.NewLine}");
        }
    }
}
