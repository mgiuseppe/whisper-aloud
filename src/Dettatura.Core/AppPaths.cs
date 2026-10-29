namespace Dettatura.Core;

/// <summary>Cartelle dell'app: impostazioni in %APPDATA%, modelli e log in %LOCALAPPDATA%.</summary>
public static class AppPaths
{
    public static string ConfigDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dettatura"));

    public static string DataDir { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dettatura"));

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string ModelsDir => Ensure(Path.Combine(DataDir, "models"));
    public static string LatencyLog => Path.Combine(DataDir, "latency.csv");
    public static string AppLog => Path.Combine(DataDir, "app.log");

    /// <summary>Cartella vuota usata come working directory della CLI claude, così non legge nessun progetto.</summary>
    public static string ClaudeWorkDir => Ensure(Path.Combine(DataDir, "claude-workdir"));

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
