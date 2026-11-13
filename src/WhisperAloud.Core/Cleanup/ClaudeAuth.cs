using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace WhisperAloud.Core.Cleanup;

/// <summary>
/// Controlla se Claude Code ha un accesso attivo con <c>claude auth status --json</c>: risponde in meno di un secondo
/// e non fa richieste al modello. Dell'output si legge solo il campo <c>loggedIn</c>; il resto (account, piano)
/// non viene letto né scritto nel log.
/// </summary>
public static class ClaudeAuth
{
    public const string NotSignedInCode = "authentication_failed";

    /// <returns>Vero o falso; null se non si riesce a saperlo (CLI vecchia, nessuna risposta, output inatteso).</returns>
    /// <exception cref="FileNotFoundException">La CLI non si trova.</exception>
    public static async Task<bool?> IsSignedInAsync(string executable, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(ExecutableResolver.Resolve(executable))
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "auth", "status", "--json" }) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("claude auth status non avviato");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await errors.ConfigureAwait(false);
            return ParseSignedIn(await output.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return null;
        }
    }

    /// <summary>Legge <c>loggedIn</c> dall'output JSON di <c>claude auth status</c>; null se manca o non è JSON.</summary>
    public static bool? ParseSignedIn(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("loggedIn", out var value) &&
                   value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
