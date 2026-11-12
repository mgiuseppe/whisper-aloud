using NAudio.Wave;

namespace WhisperAloud.Core.Transcription;

/// <summary>
/// Registrazioni salvate come WAV mono a 16 kHz, per ritrascriverle in un secondo momento.
/// Si salvano quando l'utente lo chiede (Conserva le registrazioni) e sempre quando la trascrizione fallisce.
/// </summary>
public static class Recordings
{
    public const string Extension = ".wav";

    /// <summary>Salva l'audio in <paramref name="dir"/> con data e ora nel nome e restituisce il percorso.</summary>
    /// <param name="samples">Audio mono a 16 kHz, valori tra -1 e 1, come lo riceve Whisper.</param>
    public static string Save(string dir, float[] samples, DateTime when)
    {
        Directory.CreateDirectory(dir);
        var name = when.ToString("yyyy-MM-dd_HH-mm-ss");
        var path = Path.Combine(dir, name + Extension);
        for (var i = 2; File.Exists(path); i++) path = Path.Combine(dir, $"{name}_{i}{Extension}");

        // PCM a 16 bit: metà spazio del float, nessuna perdita che conti per Whisper (~1,9 MB al minuto).
        using var writer = new WaveFileWriter(path, new WaveFormat(WhisperTranscriber.SampleRate, 16, 1));
        writer.WriteSamples(samples, 0, samples.Length);
        return path;
    }

    /// <summary>Cancella le registrazioni più vecchie di <paramref name="days"/> giorni; 0 o meno non cancella niente.</summary>
    /// <returns>Quanti file sono stati cancellati.</returns>
    public static int DeleteOlderThan(string dir, int days, DateTime now)
    {
        if (days <= 0 || !Directory.Exists(dir)) return 0;
        var limit = now - TimeSpan.FromDays(days);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            try
            {
                if (File.GetLastWriteTime(file) >= limit) continue;
                File.Delete(file);
                deleted++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // File aperto altrove: si riprova al prossimo avvio.
            }
        }
        return deleted;
    }
}
