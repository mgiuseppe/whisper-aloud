namespace WhisperAloud.Core;

/// <summary>Cartelle dell'app: impostazioni in %APPDATA%, modelli e log in %LOCALAPPDATA%.</summary>
public static class AppPaths
{
    public static string ConfigDir { get; } = Ensure(Folder(Environment.SpecialFolder.ApplicationData));

    public static string DataDir { get; } = Ensure(Folder(Environment.SpecialFolder.LocalApplicationData));

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");
    public static string ModelsDir => Ensure(Path.Combine(DataDir, "models"));
    public static string LatencyLog => Path.Combine(DataDir, "latency.csv");
    public static string AppLog => Path.Combine(DataDir, "app.log");

    /// <summary>Cartella vuota usata come working directory della CLI claude, così non legge nessun progetto.</summary>
    public static string ClaudeWorkDir => Ensure(Path.Combine(DataDir, "claude-workdir"));

    /// <summary>
    /// Cartella dell'app sotto la cartella di sistema data. L'app si chiamava "Dettatura": se trova la cartella
    /// vecchia e non ancora la nuova la sposta, così impostazioni e modello (~550 MB) non vanno rifatti.
    /// </summary>
    private static string Folder(Environment.SpecialFolder root)
    {
        var parent = Environment.GetFolderPath(root);
        var current = Path.Combine(parent, "WhisperAloud");
        var legacy = Path.Combine(parent, "Dettatura");
        try
        {
            if (!Directory.Exists(current) && Directory.Exists(legacy)) Directory.Move(legacy, current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Cartella vecchia in uso: si parte da quella nuova, la vecchia resta dov'è.
        }
        return current;
    }

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
