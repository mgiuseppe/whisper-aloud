using WhisperAloud.Core.Transcription;
using NAudio.Wave;

namespace WhisperAloud.App;

/// <summary>Registra dal microfono direttamente nel formato di Whisper (mono, 16 kHz).</summary>
internal sealed class MicRecorder : IDisposable
{
    private readonly WaveInEvent _waveIn;
    private readonly List<float> _samples = [];
    private readonly Lock _lock = new();

    public MicRecorder(int deviceNumber)
    {
        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(WhisperTranscriber.SampleRate, 16, 1),
            BufferMilliseconds = 50,
        };
        _waveIn.DataAvailable += OnDataAvailable;
    }

    public bool IsRecording { get; private set; }

    /// <summary>Energia dell'ultimo blocco audio (circa 50 ms), per l'indicatore di livello.</summary>
    public float Level { get; private set; }

    /// <summary>Microfoni visti da WaveIn: indice e nome (Windows tronca i nomi a 31 caratteri).</summary>
    public static IReadOnlyList<(int Index, string Name)> ListDevices() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount)
            .Select(i => (i, WaveInEvent.GetCapabilities(i).ProductName))
            .ToList();

    public static string DeviceName(int deviceNumber) =>
        deviceNumber >= 0 && deviceNumber < WaveInEvent.DeviceCount
            ? WaveInEvent.GetCapabilities(deviceNumber).ProductName
            : "predefinito di Windows";

    public void Start()
    {
        lock (_lock) _samples.Clear();
        Level = 0;
        _waveIn.StartRecording();
        IsRecording = true;
    }

    /// <summary>Ferma la registrazione e restituisce i campioni raccolti.</summary>
    public float[] Stop()
    {
        if (IsRecording)
        {
            _waveIn.StopRecording();
            IsRecording = false;
        }
        lock (_lock) return [.. _samples];
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        double sum = 0;
        var count = 0;
        lock (_lock)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                var sample = BitConverter.ToInt16(e.Buffer, i) / 32768f;
                _samples.Add(sample);
                sum += sample * sample;
                count++;
            }
        }
        if (count > 0) Level = (float)Math.Sqrt(sum / count);
    }

    public void Dispose() => _waveIn.Dispose();
}
