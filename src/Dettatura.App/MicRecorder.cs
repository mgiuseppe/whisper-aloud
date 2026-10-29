using Dettatura.Core.Transcription;
using NAudio.Wave;

namespace Dettatura.App;

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

    public void Start()
    {
        lock (_lock) _samples.Clear();
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
        lock (_lock)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                _samples.Add(BitConverter.ToInt16(e.Buffer, i) / 32768f);
            }
        }
    }

    public void Dispose() => _waveIn.Dispose();
}
