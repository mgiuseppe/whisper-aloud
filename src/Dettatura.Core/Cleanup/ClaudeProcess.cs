using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Dettatura.Core.Cleanup;

/// <summary>Un processo claude in stream-json che risponde a un solo messaggio.</summary>
internal sealed class ClaudeProcess
{
    private const int MaxStderrChars = 4000;
    private static readonly TimeSpan RetireGracePeriod = TimeSpan.FromSeconds(60);

    private readonly Process _process;
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>();
    private readonly StringBuilder _stderr = new();
    private readonly Stopwatch _age = Stopwatch.StartNew();
    private int _retired;

    private ClaudeProcess(Process process)
    {
        _process = process;
    }

    public TimeSpan Age => _age.Elapsed;

    public bool HasExited
    {
        get
        {
            try { return _process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }
    }

    public static ClaudeProcess Start(string executable, IEnumerable<string> arguments, string workingDirectory)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var info = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = utf8,
            StandardOutputEncoding = utf8,
            StandardErrorEncoding = utf8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var claude = new ClaudeProcess(process);
        process.ErrorDataReceived += (_, e) => claude.AppendStderr(e.Data);

        if (!process.Start()) throw new InvalidOperationException($"Impossibile avviare {executable}.");
        process.BeginErrorReadLine();
        _ = claude.PumpStdoutAsync();
        return claude;
    }

    /// <summary>Manda un messaggio e restituisce il testo della prima risposta, senza aspettare la fine del turno.</summary>
    public async Task<string> AskAsync(string content, CancellationToken cancellationToken)
    {
        await _process.StandardInput.WriteLineAsync(StreamJson.UserMessage(content).AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var line in _stdout.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            switch (StreamJson.Parse(line))
            {
                case AssistantText assistant:
                    return assistant.Text;
                case TurnResult { IsError: true } error:
                    throw new ClaudeCliException($"La CLI claude ha risposto con un errore ({error.Subtype}): {error.Result}");
                case TurnResult result:
                    return result.Result;
            }
        }

        throw new ClaudeCliException($"La CLI claude è terminata senza rispondere. {Stderr()}".Trim());
    }

    /// <summary>Chiude l'input così la CLI termina il turno ed esce da sola; la uccide se non lo fa.</summary>
    public void Retire()
    {
        if (Interlocked.Exchange(ref _retired, 1) == 1) return;

        try { _process.StandardInput.Close(); }
        catch (Exception e) when (e is IOException or InvalidOperationException) { }

        _ = Task.Run(async () =>
        {
            using var grace = new CancellationTokenSource(RetireGracePeriod);
            try { await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { Kill(); }
            finally { _process.Dispose(); }
        });
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private async Task PumpStdoutAsync()
    {
        try
        {
            while (await _process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _stdout.Writer.TryWrite(line);
            }
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ObjectDisposedException) { }
        finally
        {
            _stdout.Writer.TryComplete();
        }
    }

    private void AppendStderr(string? line)
    {
        if (line is null) return;
        lock (_stderr)
        {
            if (_stderr.Length < MaxStderrChars) _stderr.AppendLine(line);
        }
    }

    private string Stderr()
    {
        lock (_stderr) return _stderr.ToString();
    }
}

public sealed class ClaudeCliException(string message) : Exception(message);
