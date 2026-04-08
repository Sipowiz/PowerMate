using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace PowerMate.Services;

public class AudioService : IAudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;

    public event Action<float, bool>? VolumeChanged;

    // ── Bass detection via loopback capture + FFT ─────────────────────────────
    private WasapiLoopbackCapture? _capture;
    private float _bassPeak;
    private int _sampleRate;
    private int _bassMaxBin;
    private const int FftLength = 2048;           // ~23 Hz per bin at 48 kHz
    private const int FftLog2   = 11;             // log2(2048)
    private readonly Complex[] _fftBuffer = new Complex[FftLength];
    private int _fftPos;
    private int _bassCutoffHz = 250;

    private AudioEndpointVolume Endpoint
    {
        get
        {
            if (_endpointVolume == null)
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _endpointVolume = _device.AudioEndpointVolume;
                _endpointVolume.OnVolumeNotification += data =>
                    VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
            }
            return _endpointVolume;
        }
    }

    public float GetLevel() => Endpoint.MasterVolumeLevelScalar;

    public void SetLevel(float level) =>
        Endpoint.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);

    public void AdjustLevel(float delta) => SetLevel(GetLevel() + delta);

    public bool IsMuted() => Endpoint.Mute;

    public void ToggleMute()
    {
        var ep = Endpoint;
        ep.Mute = !ep.Mute;
    }

    public float GetPeakLevel()
    {
        // Access through cached _device to avoid creating new MMDevice each call
        _ = Endpoint; // ensure _device is initialized
        return _device!.AudioMeterInformation.MasterPeakValue;
    }

    // ── Bass capture ──────────────────────────────────────────────────────────
    public float GetBassPeak() => _bassPeak;

    private float _bassGain = 5.0f;

    public void StartBassCapture(int cutoffHz, float gain)
    {
        StopBassCapture();
        _bassCutoffHz = cutoffHz;
        _bassGain = gain;
        _bassPeak = 0;
        _fftPos = 0;

        _capture = new WasapiLoopbackCapture();
        _sampleRate = _capture.WaveFormat.SampleRate;
        _bassMaxBin = (int)((float)_bassCutoffHz / _sampleRate * FftLength);

        _capture.DataAvailable += OnLoopbackData;
        _capture.StartRecording();
    }

    public void StopBassCapture()
    {
        if (_capture == null) return;
        _capture.DataAvailable -= OnLoopbackData;
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
        _capture = null;
        _bassPeak = 0;
    }

    private void OnLoopbackData(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _capture!.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;
        int stride = bytesPerSample * channels;

        for (int i = 0; i + stride <= e.BytesRecorded; i += stride)
        {
            // Take first channel as float
            float sample = BitConverter.ToSingle(e.Buffer, i);

            _fftBuffer[_fftPos].X = sample;
            _fftBuffer[_fftPos].Y = 0;
            _fftPos++;

            if (_fftPos >= FftLength)
            {
                _fftPos = 0;
                FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

                // Find peak magnitude across bass bins (skip bin 0 = DC)
                float peak = 0;
                int count = Math.Min(_bassMaxBin, FftLength / 2);
                for (int b = 1; b <= count; b++)
                {
                    float mag = _fftBuffer[b].X * _fftBuffer[b].X +
                                _fftBuffer[b].Y * _fftBuffer[b].Y;
                    if (mag > peak) peak = mag;
                }

                // Convert to magnitude, scale by user gain
                float level = (float)Math.Sqrt(peak) * _bassGain;
                level = Math.Clamp(level, 0f, 1f);

                // Smooth: fast attack, slower decay
                if (level >= _bassPeak)
                    _bassPeak = level;
                else
                    _bassPeak = _bassPeak * 0.85f + level * 0.15f;
            }
        }
    }

    public void Dispose()
    {
        StopBassCapture();
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
