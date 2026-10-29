using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using Whisper.net.LibraryLoader;

namespace Dettatura.Core.Transcription;

/// <summary>Trascrizione locale con whisper.cpp (Whisper.net) sulla GPU via Vulkan, con ripiego su CPU.</summary>
public sealed class WhisperTranscriber : IAsyncDisposable
{
    public const int SampleRate = 16000;

    /// <summary>Sotto questa energia media l'audio è considerato silenzio: Whisper sul silenzio inventa frasi.</summary>
    private const float SilenceRms = 0.003f;

    private readonly WhisperFactory _factory;
    private readonly WhisperProcessor _processor;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private WhisperTranscriber(WhisperFactory factory, WhisperProcessor processor)
    {
        _factory = factory;
        _processor = processor;
    }

    /// <summary>Libreria nativa caricata (Vulkan o CPU), utile per capire se la GPU è in uso.</summary>
    public static string? LoadedRuntime => RuntimeOptions.LoadedLibrary?.ToString();

    public static async Task<WhisperTranscriber> CreateAsync(
        string modelPath, string language, string? prompt, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath))
        {
            progress?.Report("Download del modello Whisper large-v3-turbo (~550 MB)...");
            await DownloadModelAsync(modelPath, cancellationToken).ConfigureAwait(false);
        }

        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];

        progress?.Report("Caricamento del modello Whisper...");
        var factory = WhisperFactory.FromPath(modelPath);
        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithSingleSegment();
        if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);

        var transcriber = new WhisperTranscriber(factory, builder.Build());

        // La prima inferenza sulla GPU prepara le pipeline Vulkan: meglio farla ora che alla prima dettatura.
        await transcriber.TranscribeAsync(new float[SampleRate], cancellationToken, skipSilenceCheck: true).ConfigureAwait(false);
        return transcriber;
    }

    /// <param name="samples">Audio mono a 16 kHz, valori tra -1 e 1.</param>
    public async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken = default) =>
        await TranscribeAsync(samples, cancellationToken, skipSilenceCheck: false).ConfigureAwait(false);

    private async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken, bool skipSilenceCheck)
    {
        if (!skipSilenceCheck && Rms(samples) < SilenceRms) return string.Empty;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = new StringBuilder();
            await foreach (var segment in _processor.ProcessAsync(samples, cancellationToken).ConfigureAwait(false))
            {
                text.Append(segment.Text);
            }
            return text.ToString().Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    private static async Task DownloadModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        var partial = modelPath + ".download";
        await using (var model = await WhisperGgmlDownloader.Default
                         .GetGgmlModelAsync(GgmlType.LargeV3Turbo, QuantizationType.Q5_0, cancellationToken)
                         .ConfigureAwait(false))
        await using (var file = File.Create(partial))
        {
            await model.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }
        File.Move(partial, modelPath, overwrite: true);
    }

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync().ConfigureAwait(false);
        _factory.Dispose();
        _gate.Dispose();
    }
}
