using PowerMate.Models;

namespace PowerMate.Services;

public enum InteractionMode { Idle, Volume, Button, FfRw }

public class PowerMateController : IDisposable
{
    private readonly IHidService  _hid;
    private readonly IAudioService _audio;
    private readonly IMediaSessionService _media;
    private PowerMateConfig _config;

    // ── Button state ──────────────────────────────────────────────────────────
    private volatile bool _buttonDown;
    private int      _rotationStepsWhileHeld; // total steps (any direction) while held
    private bool     _ffRwActive;
    private volatile bool _longPressFired;
    private Timer?   _longPressTimer;

    // ── Multi-tap ─────────────────────────────────────────────────────────────
    private int    _tapCount;
    private Timer? _tapTimer;
    private int    _tapGeneration;

    // ── Interaction mode ──────────────────────────────────────────────────────
    private InteractionMode _interactionMode = InteractionMode.Idle;
    private Timer? _idleTimer;
    private Timer? _ffRwLedTimer;
    // Overridable by tests via InternalsVisibleTo.
    internal int IdleTimeoutMs        = 2000;
    internal int ResumeCaptureDelayMs = 2000;
    internal int FfRwReleaseGuardMs   = 500;

    private volatile bool _ffRwReleaseGuard;
    private Timer? _ffRwReleaseGuardTimer;

    // ── Audio-peak LED pulse (idle) ───────────────────────────────────────────
    private Timer? _audioPulseTimer;
    private Timer? _volumeOverrideTimer;
    private volatile bool _volumeOverrideActive;
    private volatile bool _selfChanging;
    private const int VolumeOverrideMs = 2000;

    public event Action<float, bool>?     VolumeChanged;
    public event Action<bool>?            ConnectionChanged;
    public event Action<InteractionMode>? InteractionModeChanged;
    public event Action<PlaybackState>?   SymbolFlash;

    public bool IsConnected => _hid.IsConnected;

    public IMediaSessionService Media => _media;

    public PowerMateController(IHidService hid, IAudioService audio,
        IMediaSessionService media, PowerMateConfig config)
    {
        _hid    = hid;
        _audio  = audio;
        _media  = media;
        _config = config;

        _hid.Rotated           += OnRotated;
        _hid.ButtonPressed     += OnButtonPressed;
        _hid.ButtonReleased    += OnButtonReleased;
        _hid.ConnectionChanged += c => ConnectionChanged?.Invoke(c);
        _audio.VolumeChanged   += OnSystemVolumeChanged;
    }

    public void Start()
    {
        _hid.Start();
        _audio.GetLevel(); // init volume endpoint / start notification listener
        ApplyAudioPulse();
    }

    public void UpdateConfig(PowerMateConfig config)
    {
        _config = config;
        ApplyAudioPulse();
        if (!_config.LedPulseOnAudio)
            _hid.SetLed((byte)(_audio.GetLevel() * 255));
    }

    // ── Interaction mode ──────────────────────────────────────────────────────

    private void SetInteractionMode(InteractionMode mode)
    {
        if (_interactionMode == mode) return;
        _interactionMode = mode;
        InteractionModeChanged?.Invoke(mode);

        _ffRwLedTimer?.Dispose();
        _ffRwLedTimer = null;

        if (mode == InteractionMode.FfRw)
            _ffRwLedTimer = new Timer(FfRwLedTick, null, 0, 100);
    }

    private void ResetIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = new Timer(_ =>
        {
            SetInteractionMode(InteractionMode.Idle);
            // Restore audio pulse LED (pulse timer already running if configured;
            // tick will no-op while not Idle, and now will resume)
            if (!_config.LedPulseOnAudio)
                _hid.SetLed((byte)(_audio.GetLevel() * 255));
        }, null, IdleTimeoutMs, Timeout.Infinite);
    }

    // ── Volume change from system ─────────────────────────────────────────────

    private void OnSystemVolumeChanged(float level, bool muted)
    {
        if (_selfChanging) return;
        if (!_config.LedPulseOnAudio || _volumeOverrideActive)
            _hid.SetLed((byte)(level * 255));
        VolumeChanged?.Invoke(level, muted);
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private void OnRotated(int direction)
    {
        if (_buttonDown)
        {
            Interlocked.Increment(ref _rotationStepsWhileHeld);

            if (!_ffRwActive && _rotationStepsWhileHeld >= _config.FfRwThreshold)
            {
                _ffRwActive = true;
                // Cancel long-press and tap timers — FF/RW takes over.
                _longPressTimer?.Dispose(); _longPressTimer = null;
                _tapTimer?.Dispose(); _tapTimer = null;
                Interlocked.Exchange(ref _tapCount, 0);
                Interlocked.Increment(ref _tapGeneration);
                SetInteractionMode(InteractionMode.FfRw);
            }

            if (_ffRwActive)
            {
                int d = _config.InvertRotation ? -direction : direction;
                _ = _media.SeekRelativeAsync(
                    TimeSpan.FromSeconds(d * _config.FfRwStepSeconds));
                // FF/RW stays active while the button is held; button release handles exit.
            }
            return;
        }

        // Normal volume – ignore during multi-tap window or FF/RW release guard
        if (Volatile.Read(ref _tapCount) > 0) return;
        if (_ffRwReleaseGuard) return;

        int delta = _config.InvertRotation ? -direction : direction;
        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;

        _selfChanging = true;
        _audio.AdjustLevel(delta * step);
        _selfChanging = false;

        float vol   = _audio.GetLevel();
        bool  muted = _audio.IsMuted();

        _hid.SetLed((byte)(vol * 255));
        if (_config.LedPulseOnAudio)
        {
            _volumeOverrideActive = true;
            _volumeOverrideTimer?.Dispose();
            _volumeOverrideTimer = new Timer(_ => _volumeOverrideActive = false,
                null, VolumeOverrideMs, Timeout.Infinite);
        }

        VolumeChanged?.Invoke(vol, muted);
        SetInteractionMode(InteractionMode.Volume);
        ResetIdleTimer();
    }

    // ── Button ────────────────────────────────────────────────────────────────

    private void OnButtonPressed()
    {
        _buttonDown             = true;
        _rotationStepsWhileHeld = 0;
        _ffRwActive               = false;
        _longPressFired           = false;

        // Cancel idle timer while button is held
        _idleTimer?.Dispose();
        _idleTimer = null;

        // Fire mute immediately when the long-press threshold is crossed,
        // so the user gets audio feedback without waiting for release.
        _longPressTimer?.Dispose();
        _longPressTimer = new Timer(_ =>
        {
            if (_buttonDown && !_ffRwActive && !_longPressFired)
            {
                _longPressFired = true;
                _tapTimer?.Dispose(); _tapTimer = null;
                Interlocked.Exchange(ref _tapCount, 0);
                Interlocked.Increment(ref _tapGeneration);
                DoToggleMute();
            }
        }, null, _config.LongPressMs, Timeout.Infinite);

        SetInteractionMode(InteractionMode.Button);
    }

    private void OnButtonReleased()
    {
        if (!_buttonDown) return;
        _buttonDown = false;

        _longPressTimer?.Dispose();
        _longPressTimer = null;

        if (_ffRwActive)
        {
            _ffRwActive = false;
            SetInteractionMode(InteractionMode.Idle);
            if (!_config.LedPulseOnAudio)
                _hid.SetLed((byte)(_audio.GetLevel() * 255));

            _ffRwReleaseGuard = true;
            _ffRwReleaseGuardTimer?.Dispose();
            _ffRwReleaseGuardTimer = new Timer(_ => _ffRwReleaseGuard = false,
                null, FfRwReleaseGuardMs, Timeout.Infinite);
            return;
        }

        if (_longPressFired)
        {
            // Action already executed; wait for release was the only requirement.
            _longPressFired = false;
            ResetIdleTimer();
            return;
        }

        // Short tap
        Interlocked.Increment(ref _tapCount);
        int gen = Interlocked.Increment(ref _tapGeneration);
        _tapTimer?.Dispose();
        _tapTimer = new Timer(_ =>
        {
            if (Volatile.Read(ref _tapGeneration) != gen) return;
            if (!_buttonDown)
                ExecuteTaps();
            else
                Interlocked.Exchange(ref _tapCount, 0); // discard taps that straddle a held press
        }, null, _config.TapWindowMs, Timeout.Infinite);

        ResetIdleTimer();
    }

    private void ExecuteTaps()
    {
        int count = Interlocked.Exchange(ref _tapCount, 0);
        switch (count)
        {
            case 1:
                MediaKeyService.PlayPause();
                break;
            case 2:
                MediaKeyService.NextTrack();
                SymbolFlash?.Invoke(PlaybackState.SkipNext);
                break;
            case >= 3:
                MediaKeyService.PreviousTrack();
                SymbolFlash?.Invoke(PlaybackState.SkipPrev);
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

    // ── FF/RW LED tick ────────────────────────────────────────────────────────

    private void FfRwLedTick(object? _)
    {
        if (!_hid.IsConnected) return;
        float pos = _media.GetPlaybackPosition();
        _hid.SetLed((byte)(pos * 255));
    }

    // ── Audio-peak LED pulse (idle mode) ──────────────────────────────────────

    private void ApplyAudioPulse()
    {
        if (_config.LedPulseOnAudio)
        {
            if (_config.LedBassOnly)
                _audio.StartBassCapture(_config.BassFrequencyCutoff, _config.BassGain);
            else
                _audio.StartPeakCapture();

            _audioPulseTimer ??= new Timer(AudioPulseTick, null, 0, 20);
        }
        else
        {
            _audioPulseTimer?.Dispose();
            _audioPulseTimer = null;
            _audio.StopCapture();
        }
    }

    private void AudioPulseTick(object? _)
    {
        // Only drive LED from audio when we're truly idle and not in a volume override
        if (!_hid.IsConnected
            || _volumeOverrideActive
            || _interactionMode != InteractionMode.Idle) return;

        // Fall back to static volume indicator when nothing is playing
        if (_media.GetPlaybackState() != PlaybackState.Playing)
        {
            _hid.SetLed((byte)(_audio.GetLevel() * 255));
            return;
        }

        float peak = _config.LedBassOnly
            ? _audio.GetBassPeak()
            : _audio.GetPeakLevel();
        _hid.SetLed((byte)(peak * 255));
    }

    // ── Power management ──────────────────────────────────────────────────────

    public void Suspend()
    {
        _audioPulseTimer?.Dispose();
        _audioPulseTimer = null;
        _audio.StopCapture();
    }

    public void Resume()
    {
        // Delay audio restart to allow WASAPI to reinitialize after sleep/hibernate
        _ = Task.Delay(ResumeCaptureDelayMs).ContinueWith(_ => ApplyAudioPulse());
    }

    public void Dispose()
    {
        _longPressTimer?.Dispose();
        _tapTimer?.Dispose();
        _volumeOverrideTimer?.Dispose();
        _audioPulseTimer?.Dispose();
        _ffRwLedTimer?.Dispose();
        _ffRwReleaseGuardTimer?.Dispose();
        _idleTimer?.Dispose();
        _hid.Dispose();
        _audio.Dispose();
        _media.Dispose();
    }
}
