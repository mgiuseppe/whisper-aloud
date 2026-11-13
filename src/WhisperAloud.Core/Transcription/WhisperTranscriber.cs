using System.Text;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace WhisperAloud.Core.Transcription;

/// <summary>Trascrizione locale con whisper.cpp (Whisper.net) sulla GPU via Vulkan, con ripiego su CPU.</summary>
public sealed class WhisperTranscriber : IAsyncDisposable
{
    public const int SampleRate = 16000;

    /// <summary>Sotto questa energia media l'audio è considerato silenzio: Whisper sul silenzio inventa frasi.</summary>
    public const float SilenceRms = 0.003f;

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
        string modelPath, string language, string? prompt, IProgress<string>? progress = null,
        int gpuDevice = -1, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(modelPath))
        {
            progress?.Report(Loc.T("Download del modello Whisper (~550 MB)...", "Downloading the Whisper model (~550 MB)..."));
            await DownloadModelAsync(modelPath, progress, cancellationToken).ConfigureAwait(false);
        }

        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        // Le righe sui dispositivi servono solo al caricamento: whisper.cpp le ripete a ogni trascrizione.
        var loading = true;
        if (log is not null) LogProvider.AddLogger((_, message) => { if (loading && message is not null && IsDeviceLine(message)) log(message.Trim()); });

        progress?.Report(Loc.T("Caricamento del modello Whisper...", "Loading the Whisper model..."));
        // Con -1 si sceglie la prima GPU dedicata: whisper.cpp userebbe la GPU 0, sui portatili spesso l'integrata.
        if (gpuDevice < 0 && VulkanDevices.FindDiscreteGpu() is { } discrete)
        {
            gpuDevice = discrete.Index;
            log?.Invoke($"GPU dedicata scelta: {discrete.Index} = {discrete.Name}");
        }
        var options = new WhisperFactoryOptions();
        if (gpuDevice >= 0) options.GpuDevice = gpuDevice;
        var factory = WhisperFactory.FromPath(modelPath, options);
        var builder = factory.CreateBuilder()
            .WithLanguage(language)
            .WithNoContext()
            .WithSingleSegment();
        if (!string.IsNullOrWhiteSpace(prompt)) builder = builder.WithPrompt(prompt);

        var transcriber = new WhisperTranscriber(factory, builder.Build());

        // La prima inferenza sulla GPU prepara le pipeline Vulkan: meglio farla ora che alla prima dettatura.
        await transcriber.TranscribeAsync(new float[SampleRate], cancellationToken, skipSilenceCheck: true).ConfigureAwait(false);
        loading = false;
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

    private static bool IsDeviceLine(string message) =>
        message.Contains("vulkan", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("device", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("gpu", StringComparison.OrdinalIgnoreCase);

    /// <summary>Energia media dell'audio: sotto <see cref="SilenceRms"/> non si trascrive.</summary>
    public static float Rms(float[] samples)
    {
        if (samples.Length == 0) return 0;
        double sum = 0;
        foreach (var s in samples) sum += s * s;
        return (float)Math.Sqrt(sum / samples.Length);
    }

    /// <summary>Indirizzo del modello predefinito su Hugging Face (lo stesso usato dal downloader di Whisper.net).</summary>
    public const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin";

    /// <summary>Scarica il modello in un file .download e lo rinomina solo a fine download, riportando i MB scaricati.</summary>
    private static async Task DownloadModelAsync(string modelPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        var partial = modelPath + ".download";
        using (var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan })
        using (var response = await http.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(partial);
            var buffer = new byte[1 << 20];
            long done = 0, lastReportedMb = -1;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                var mb = done >> 20;
                if (progress is null || mb - lastReportedMb < 5) continue; // un aggiornamento ogni 5 MB basta
                lastReportedMb = mb;
                progress.Report(DownloadStatus(mb, total is { } t ? t >> 20 : null));
            }
        }
        File.Move(partial, modelPath, overwrite: true);
    }

    /// <summary>Testo di avanzamento del download, es. "Download del modello Whisper: 230 / 548 MB (42%)".</summary>
    public static string DownloadStatus(long doneMb, long? totalMb) => totalMb is > 0 and var total
        ? Loc.T($"Download del modello Whisper: {doneMb} / {total} MB ({doneMb * 100 / total}%)", $"Downloading the Whisper model: {doneMb} / {total} MB ({doneMb * 100 / total}%)")
        : Loc.T($"Download del modello Whisper: {doneMb} MB", $"Downloading the Whisper model: {doneMb} MB");

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync().ConfigureAwait(false);
        _factory.Dispose();
        _gate.Dispose();
    }
}
