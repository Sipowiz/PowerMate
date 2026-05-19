using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Windows.Media.Devices;

namespace PowerMate.Services;

public class AudioService : IAudioService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDevice? _device;
    private AudioEndpointVolume? _endpointVolume;
    private readonly object _endpointLock = new();

    public event Action<float, bool>? VolumeChanged;

    public AudioService()
    {
        // WinRT event fires whenever the system default render device changes
        MediaDevice.DefaultAudioRenderDeviceChanged += OnDefaultRenderDeviceChanged;
    }

    private void OnDefaultRenderDeviceChanged(object? sender,
        DefaultAudioRenderDeviceChangedEventArgs e)
    {
        if (e.Role != AudioDeviceRole.Default) return;
        ResetEndpoint();
        ResetMeterDevice();
        VolumeChanged?.Invoke(GetLevel(), IsMuted());
    }

    private void ResetEndpoint()
    {
        lock (_endpointLock)
        {
            try { _endpointVolume?.Dispose(); } catch { }
            try { _device?.Dispose(); } catch { }
            _endpointVolume = null;
            _device         = null;
        }
    }

    // ── Loopback capture state ──────────────────────────────────────────────────
    private WasapiLoopbackCapture? _capture;
    private int _sampleRate;
    private int _bassMaxBin;
    private const int FftLength = 2048;
    private const int FftLog2   = 11;
    private readonly Complex[] _fftBuffer = new Complex[FftLength];
    private int _fftPos;
    private int _bassCutoffHz = 250;

    private AudioEndpointVolume Endpoint
    {
        get
        {
            lock (_endpointLock)
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

    // ── Peak metering (needs its own device – COM apartment isolation) ─────────
    private MMDevice? _meterDevice;
    private readonly object _meterLock = new();

    public float GetPeakLevel()
    {
        // Prefer capture-based peak (volume-normalized, low latency)
        if (_capture != null) return _peak;

        // Fallback to COM meter if capture not running
        lock (_meterLock)
        {
            try
            {
                _meterDevice ??= _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return _meterDevice.AudioMeterInformation.MasterPeakValue;
            }
            catch
            {
                try { _meterDevice?.Dispose(); } catch { }
                _meterDevice = null;
                return 0f;
            }
        }
    }

    private void ResetMeterDevice()
    {
        lock (_meterLock)
        {
            try { _meterDevice?.Dispose(); } catch { }
            _meterDevice = null;
        }
    }

    // ── Loopback capture (peak + bass) ────────────────────────────────────────
    public float GetBassPeak() => _bassPeak;

    private float _bassGain = 5.0f;
    private volatile float _peak;
    private volatile float _bassPeak;
    private bool _bassMode;

    public void StartPeakCapture()
    {
        StopCapture();
        _bassMode = false;
        _peak     = 0;
        StartCaptureInternal();
    }

    public void StartBassCapture(int cutoffHz, float gain)
    {
        StopCapture();
        _bassCutoffHz = cutoffHz;
        _bassGain     = gain;
        _bassPeak     = 0;
        _peak         = 0;
        _fftPos       = 0;
        _bassMode     = true;
        StartCaptureInternal();
    }

    private void StartCaptureInternal()
    {
        _capture = new WasapiLoopbackCapture();
        _sampleRate = _capture.WaveFormat.SampleRate;
        _bassMaxBin = (int)((float)_bassCutoffHz / _sampleRate * FftLength);

        _capture.DataAvailable += OnLoopbackData;
        _capture.StartRecording();
    }

    public void StopCapture()
    {
        if (_capture == null) return;
        _capture.DataAvailable -= OnLoopbackData;
        try { _capture.StopRecording(); } catch { }
        _capture.Dispose();
        _capture  = null;
        _bassPeak = 0;
        _peak     = 0;
    }

    private void OnLoopbackData(object? sender, WaveInEventArgs e)
    {
        int bytesPerSample = _capture!.WaveFormat.BitsPerSample / 8;
        int channels = _capture.WaveFormat.Channels;
        int stride   = bytesPerSample * channels;

        // Volume normalization: divide by current volume to make peak independent
        float vol = 1f;
        try { vol = Math.Max(Endpoint.MasterVolumeLevelScalar, 0.01f); } catch { }

        float sumSquares = 0f;
        int sampleCount = 0;

        for (int i = 0; i + stride <= e.BytesRecorded; i += stride)
        {
            float sample = BitConverter.ToSingle(e.Buffer, i);
            sumSquares += sample * sample;
            sampleCount++;

            if (_bassMode)
            {
                _fftBuffer[_fftPos].X = sample;
                _fftBuffer[_fftPos].Y = 0;
                _fftPos++;

                if (_fftPos >= FftLength)
                {
                    _fftPos = 0;
                    FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

                    float bassPeakMag = 0;
                    int count = Math.Min(_bassMaxBin, FftLength / 2);
                    for (int b = 1; b <= count; b++)
                    {
                        float mag = _fftBuffer[b].X * _fftBuffer[b].X
                                  + _fftBuffer[b].Y * _fftBuffer[b].Y;
                        if (mag > bassPeakMag) bassPeakMag = mag;
                    }

                    float bassLevel = (float)Math.Sqrt(bassPeakMag) / vol * _bassGain;
                    bassLevel = Math.Clamp(bassLevel, 0f, 1f);

                    _bassPeak = bassLevel >= _bassPeak
                        ? bassLevel
                        : _bassPeak * 0.55f + bassLevel * 0.45f;
                }
            }
        }

        // RMS = true energy level of what's coming out, normalized by volume
        if (sampleCount > 0)
        {
            float rms = (float)Math.Sqrt(sumSquares / sampleCount);
            // Scale RMS to use full LED range (pure sine wave RMS ≈ 0.707 of peak)
            float level = Math.Clamp(rms * 1.8f / vol, 0f, 1f);
            _peak = level;
        }
    }

    public void Dispose()
    {
        MediaDevice.DefaultAudioRenderDeviceChanged -= OnDefaultRenderDeviceChanged;
        StopCapture();
        ResetMeterDevice();
        ResetEndpoint();
        _enumerator.Dispose();
    }
}
