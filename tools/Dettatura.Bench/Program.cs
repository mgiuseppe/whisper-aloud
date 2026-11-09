// Misura la pipeline: trascrizione Whisper dei file audio in samples/ e riscrittura con la CLI claude
// delle frasi in samples/trascrizioni.txt, confrontando un processo nuovo per ogni chiamata
// con il processo di riserva già avviato. Stampa anche prima/dopo per controllare la qualità.
// Uso: dotnet run -c Release --project tools/Dettatura.Bench [-- [modello] [--gpu N] [--solo-whisper]]   (default: sonnet)

using System.Diagnostics;
using Dettatura.Core;
using Dettatura.Core.Cleanup;
using Dettatura.Core.Transcription;

const int TranscribeRuns = 3;
const int ColdRuns = 3;
// Pausa tra una dettatura e l'altra, come nell'uso reale: dà al processo di riserva il tempo di avviarsi.
var pauseBetweenDictations = TimeSpan.FromSeconds(6);

var samplesDir = Path.Combine(FindRepoRoot(), "samples");
var files = Directory.GetFiles(samplesDir, "*.wav");
var sentences = File.ReadAllLines(Path.Combine(samplesDir, "trascrizioni.txt")).Where(l => l.Length > 0).ToList();
var settings = new DettaturaSettings();
var gpuIndex = Array.IndexOf(args, "--gpu");
var gpu = gpuIndex >= 0 ? int.Parse(args[gpuIndex + 1]) : -1;
var onlyWhisper = args.Contains("--solo-whisper");
var model = args.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _)) ?? settings.Claude.Model;

Console.WriteLine($"Modello: {settings.WhisperModel}");
var load = Stopwatch.StartNew();
await using var transcriber = await WhisperTranscriber.CreateAsync(
    settings.ResolveModelPath(), settings.Language, settings.WhisperPrompt, new Progress<string>(Console.WriteLine),
    gpu, message => Console.WriteLine($"  whisper: {message}"));
Console.WriteLine($"Whisper pronto in {load.ElapsedMilliseconds} ms (runtime: {WhisperTranscriber.LoadedRuntime})\n");

Console.WriteLine("| File | Audio | Trascrizione (mediana) |");
Console.WriteLine("|---|---|---|");
foreach (var file in files)
{
    var samples = AudioFile.ReadForWhisper(file);
    var times = new List<long>();
    var text = string.Empty;
    for (var i = 0; i < TranscribeRuns; i++)
    {
        var sw = Stopwatch.StartNew();
        text = await transcriber.TranscribeAsync(samples);
        times.Add(sw.ElapsedMilliseconds);
    }
    Console.WriteLine($"Whisper: {text}");
    Console.WriteLine($"| {Path.GetFileName(file)} | {samples.Length / (double)WhisperTranscriber.SampleRate:0.0} s | {Median(times)} ms |");
}

if (onlyWhisper) return;

var cold = await MeasureCleanupAsync(new ClaudeOptions { Model = model, UseWarmProcess = false }, ColdRuns, TimeSpan.Zero);
var warm = await MeasureCleanupAsync(new ClaudeOptions { Model = model, UseWarmProcess = true }, sentences.Count, pauseBetweenDictations);

Console.WriteLine();
Console.WriteLine($"| Riscrittura con claude ({model}) | Mediana | Min | Max |");
Console.WriteLine("|---|---|---|---|");
Console.WriteLine($"| Processo nuovo a ogni dettatura | {Median(cold)} ms | {cold.Min()} ms | {cold.Max()} ms |");
Console.WriteLine($"| Processo di riserva già avviato | {Median(warm)} ms | {warm.Min()} ms | {warm.Max()} ms |");
return;

async Task<List<long>> MeasureCleanupAsync(ClaudeOptions options, int runs, TimeSpan pause)
{
    var times = new List<long>();
    await using var cleaner = new ClaudeCleaner(options);
    for (var i = 0; i < runs; i++)
    {
        await Task.Delay(pause);
        var input = sentences[i % sentences.Count];
        var sw = Stopwatch.StartNew();
        var cleaned = await cleaner.CleanAsync(input);
        times.Add(sw.ElapsedMilliseconds);
        Console.WriteLine($"\n[{(options.UseWarmProcess ? "riserva" : "nuovo")}, {sw.ElapsedMilliseconds} ms]");
        Console.WriteLine($"  prima: {input}");
        Console.WriteLine($"  dopo:  {cleaned}");
    }
    return times;
}

static long Median(List<long> values)
{
    var sorted = values.Order().ToList();
    return sorted[sorted.Count / 2];
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Dettatura.slnx")))
    {
        dir = dir.Parent;
    }
    return dir?.FullName ?? throw new DirectoryNotFoundException("Radice del repository non trovata.");
}
