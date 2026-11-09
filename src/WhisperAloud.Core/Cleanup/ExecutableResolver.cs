namespace WhisperAloud.Core.Cleanup;

/// <summary>Trova l'eseguibile della CLI: percorso esplicito, PATH, oppure l'installazione nativa in ~\.local\bin.</summary>
public static class ExecutableResolver
{
    public static string Resolve(string executable)
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable)
                ? executable
                : throw new FileNotFoundException(Loc.T($"CLI claude non trovata in {executable}.", $"claude CLI not found at {executable}."), executable);
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        var candidates = Path.HasExtension(executable) ? [executable] : extensions.Select(ext => executable + ext.ToLowerInvariant());

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin"));

        foreach (var directory in directories)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(directory.Trim('"'), candidate);
                if (File.Exists(path)) return path;
            }
        }

        throw new FileNotFoundException(
            Loc.T($"CLI '{executable}' non trovata nel PATH. Installa Claude Code o indica il percorso in Claude.Executable nelle impostazioni.",
                $"CLI '{executable}' not found on the PATH. Install Claude Code or set its path in the settings."));
    }
}
