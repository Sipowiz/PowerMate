using PowerMate.Models;

namespace PowerMate.Services;

public class PowerMateController : IDisposable
{
    private readonly IHidService  _hid;
    private readonly IAudioService _audio;
    private PowerMateConfig _config;

    private DateTime _pressTime;
    private bool     _buttonDown;

    // ── Multi-tap ─────────────────────────────────────────────────────────────
    private int    _tapCount;
    private Timer? _tapTimer;

    // ── Audio-peak LED pulse ──────────────────────────────────────────────────
    private Timer? _audioPulseTimer;
    private Timer? _volumeOverrideTimer;
    private volatile bool _volumeOverrideActive;
    private volatile bool _selfChanging; // suppress feedback from our own volume changes
    private const int VolumeOverrideMs = 2000;

    // (volume 0-1, isMuted)
    public event Action<float, bool>? VolumeChanged;
    public event Action<bool>?        ConnectionChanged;

    public bool IsConnected => _hid.IsConnected;

    public PowerMateController(IHidService hid, IAudioService audio, PowerMateConfig config)
    {
        _hid   = hid;
        _audio = audio;
        _config = config;

        _hid.Rotated          += OnRotated;
        _hid.ButtonPressed    += OnButtonPressed;
        _hid.ButtonReleased   += OnButtonReleased;
        _hid.ConnectionChanged += c => ConnectionChanged?.Invoke(c);
        _audio.VolumeChanged  += OnSystemVolumeChanged;
    }

    public void Start()
    {
        _hid.Start();
        // Access audio level to ensure the volume notification listener is initialized
        _audio.GetLevel();
        ApplyAudioPulse();
    }

    public void UpdateConfig(PowerMateConfig config)
    {
        _config = config;
        ApplyAudioPulse();
        if (!_config.LedPulseOnAudio)
            _hid.SetLed((byte)(_audio.GetLevel() * 255));
    }

    // ── Rotation ──────────────────────────────────────────────────────────────
    private void OnSystemVolumeChanged(float level, bool muted)
    {
        // Ignore notifications caused by our own AdjustLevel/ToggleMute calls
        if (_selfChanging) return;

        if (!_config.LedPulseOnAudio || _volumeOverrideActive)
            _hid.SetLed((byte)(level * 255));

        VolumeChanged?.Invoke(level, muted);
    }

    private void OnRotated(int direction)
    {
        // Ignore rotation while a multi-tap sequence is in progress
        // to prevent accidental knob movement from disrupting click detection
        if (_tapCount > 0) return;

        int d = _config.InvertRotation ? -direction : direction;
        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;

        _selfChanging = true;
        _audio.AdjustLevel(d * step);
        _selfChanging = false;

        float level = _audio.GetLevel();
        bool  muted = _audio.IsMuted();

        _hid.SetLed((byte)(level * 255));
        if (_config.LedPulseOnAudio)
        {
            _volumeOverrideActive = true;
            _volumeOverrideTimer?.Dispose();
            _volumeOverrideTimer = new Timer(_ => _volumeOverrideActive = false,
                null, VolumeOverrideMs, Timeout.Infinite);
        }

        VolumeChanged?.Invoke(level, muted);
    }

    // ── Button ────────────────────────────────────────────────────────────────
    private void OnButtonPressed()
    {
        _buttonDown = true;
        _pressTime  = DateTime.UtcNow;
    }

    private void OnButtonReleased()
    {
        if (!_buttonDown) return;
        _buttonDown = false;

        bool isLong = (DateTime.UtcNow - _pressTime).TotalMilliseconds >= _config.LongPressMs;

        if (isLong)
        {
            // Cancel any pending tap sequence
            _tapTimer?.Dispose(); _tapTimer = null;
            _tapCount = 0;

            if (_config.LongPressAction == LongPressAction.Mute)
            {
                _selfChanging = true;
                _audio.ToggleMute();
                _selfChanging = false;
                VolumeChanged?.Invoke(_audio.GetLevel(), _audio.IsMuted());
            }
            else if (_config.LongPressAction == LongPressAction.PlayPause)
            {
                MediaKeyService.PlayPause();
            }
        }
        else
        {
            _tapCount++;
            _tapTimer?.Dispose();
            _tapTimer = new Timer(_ => ExecuteTaps(), null, _config.TapWindowMs, Timeout.Infinite);
        }
    }

    private void ExecuteTaps()
    {
        int count = Interlocked.Exchange(ref _tapCount, 0);
        switch (count)
        {
            case 1:
                switch (_config.ClickAction)
                {
                    case ClickAction.PlayPause:
                        MediaKeyService.PlayPause();
                        break;
                    case ClickAction.Mute:
                        DoToggleMute();
                        break;
                }
                break;
            case 2:
                switch (_config.DoubleClickAction)
                {
                    case DoubleClickAction.NextTrack:
                        MediaKeyService.NextTrack();
                        break;
                    case DoubleClickAction.PlayPause:
                        MediaKeyService.PlayPause();
                        break;
                    case DoubleClickAction.Mute:
                        DoToggleMute();
                        break;
                }
                break;
            case >= 3:
                switch (_config.TripleClickAction)
                {
                    case TripleClickAction.PreviousTrack:
                        MediaKeyService.PreviousTrack();
                        break;
                    case TripleClickAction.PlayPause:
                        MediaKeyService.PlayPause();
                        break;
                    case TripleClickAction.Mute:
                        DoToggleMute();
                        break;
                }
                break;
        }
    }

    private void DoToggleMute()
    {
        _selfChanging = true;
        _audio.ToggleMute();
        _selfChanging = false;
        VolumeChanged?.Invoke(_audio.GetLevel(), _audio.IsMuted());
    }

    // ── Audio-peak LED pulse ──────────────────────────────────────────────────
    private void ApplyAudioPulse()
    {
        if (_config.LedPulseOnAudio)
        {
            if (_config.LedBassOnly)
                _audio.StartBassCapture(_config.BassFrequencyCutoff, _config.BassGain);
            else
                _audio.StopBassCapture();

            _audioPulseTimer ??= new Timer(AudioPulseTick, null, 0, 80);
        }
        else
        {
            _audioPulseTimer?.Dispose();
            _audioPulseTimer = null;
            _audio.StopBassCapture();
        }
    }

    private void AudioPulseTick(object? _)
    {
        if (!_hid.IsConnected || _volumeOverrideActive) return;
        float peak = _config.LedBassOnly
            ? _audio.GetBassPeak()
            : _audio.GetPeakLevel();
        _hid.SetLed((byte)(peak * 255));
    }

    public void Dispose()
    {
        _tapTimer?.Dispose();
        _volumeOverrideTimer?.Dispose();
        _audioPulseTimer?.Dispose();
        _hid.Dispose();
        _audio.Dispose();
    }
}
