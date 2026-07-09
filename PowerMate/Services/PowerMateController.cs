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
        _hid.ConnectionChanged += OnHidConnectionChanged;
        _audio.VolumeChanged   += OnSystemVolumeChanged;
    }

    // ── LED ───────────────────────────────────────────────────────────────────

    // The LED encodes its value as brightness, so LedBrightness acts as the
    // maximum: level scales linearly within it rather than offsetting it.
    private void WriteLed(float level)
    {
        int value = (int)(Math.Clamp(level, 0f, 1f) * _config.LedBrightness);
        _hid.SetLed((byte)Math.Clamp(value, 0, 255));
    }

    // ── Connection ────────────────────────────────────────────────────────────

    private void OnHidConnectionChanged(bool connected)
    {
        if (connected)
        {
            // HidService lights the LED at a fixed level on connect; restore ours.
            if (!_config.LedPulseOnAudio) WriteLed(_audio.GetLevel());
        }
        else
        {
            ResetInputState();
        }
        ConnectionChanged?.Invoke(connected);
    }

    // A disconnect mid-gesture would otherwise leave the button latched down and a
    // tap queued, so the next real press is counted as a second tap.
    private void ResetInputState()
    {
        _buttonDown             = false;
        _longPressFired         = false;
        _ffRwActive             = false;
        _rotationStepsWhileHeld = 0;
        StopFfRw(graceful: false); // device is gone; do not seek on the way out

        _longPressTimer?.Dispose(); _longPressTimer = null;
        _tapTimer?.Dispose();       _tapTimer       = null;
        Interlocked.Exchange(ref _tapCount, 0);
        Interlocked.Increment(ref _tapGeneration);

        SetInteractionMode(InteractionMode.Idle);
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
            WriteLed(_audio.GetLevel());
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
                WriteLed(_audio.GetLevel());
        }, null, IdleTimeoutMs, Timeout.Infinite);
    }

    // ── Volume change from system ─────────────────────────────────────────────

    private void OnSystemVolumeChanged(float level, bool muted)
    {
        if (_selfChanging) return;
        if (!_config.LedPulseOnAudio || _volumeOverrideActive)
            WriteLed(level);
        VolumeChanged?.Invoke(level, muted);
    }

    // ── Rotation ──────────────────────────────────────────────────────────────

    private void OnRotated(int direction)
    {
        if (_buttonDown)
        {
            // Magnitude only: a CW step followed by a CCW step still means the user
            // is spinning, so both count toward entering FF/RW.
            int total = Interlocked.Increment(ref _rotationStepsWhileHeld);

            if (!_ffRwActive && total >= _config.FfRwThreshold)
            {
                _ffRwActive = true;
                // Cancel long-press and tap timers — FF/RW takes over.
                _longPressTimer?.Dispose(); _longPressTimer = null;
                _tapTimer?.Dispose(); _tapTimer = null;
                Interlocked.Exchange(ref _tapCount, 0);
                Interlocked.Increment(ref _tapGeneration);
                SetInteractionMode(InteractionMode.FfRw);
                EnterFfRw();
            }

            if (_ffRwActive)
            {
                _ffRwSteps += _config.InvertRotation ? -direction : direction;
                UpdateFfRwTarget();
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

        WriteLed(vol);
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
            StopFfRw(graceful: true);
            SetInteractionMode(InteractionMode.Idle);
            if (!_config.LedPulseOnAudio)
                WriteLed(_audio.GetLevel());

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

    // ── FF/RW seeking ─────────────────────────────────────────────────────────
    //
    // SMTC keeps reporting the pre-seek position for tens of milliseconds after a
    // seek lands, so asking it "where am I now?" once per detent makes every step
    // compute from the same stale base — the position oscillates instead of
    // advancing (issue #4). Instead the anchor is captured exactly once when the
    // gesture starts and never re-read, detents accumulate into a signed offset,
    // and a single pump applies the latest absolute target with at most one seek
    // in flight. All per-gesture state lives in one object so a pump still
    // draining from the previous gesture can never act on the next one's target.

    private sealed class FfRwGesture
    {
        public readonly SemaphoreSlim Signal = new(0, 1);
        public readonly CancellationTokenSource Cts = new();
        public long AnchorTicks;
        public long DurationTicks;
        public long TargetTicks;
        public int  GenerationAtEntry;
        public volatile bool StopRequested;
        public volatile bool SeekSupported = true;
        public Task? Pump;
    }

    private FfRwGesture? _ffRw;   // swapped via Interlocked; read via Volatile
    private int _ffRwSteps;       // signed detent offset; HID thread only

    private void EnterFfRw()
    {
        _ffRwSteps = 0; // detents spent crossing the threshold don't also seek

        // GetPosition/GetDuration are synchronous cache reads. If they ever become
        // blocking COM calls this must move off the HID poll thread.
        var g = new FfRwGesture
        {
            AnchorTicks       = _media.GetPosition().Ticks,
            DurationTicks     = _media.GetDuration().Ticks,
            GenerationAtEntry = _media.GetSessionGeneration(),
        };
        g.TargetTicks = g.AnchorTicks;
        Interlocked.Exchange(ref _ffRw, g);
        g.Pump = Task.Run(() => FfRwPumpAsync(g));
    }

    // HID thread only.
    private void UpdateFfRwTarget()
    {
        var g = Volatile.Read(ref _ffRw);
        if (g == null) return;

        long stepTicks = (long)_config.FfRwStepSeconds * TimeSpan.TicksPerSecond;
        long target    = g.AnchorTicks + _ffRwSteps * stepTicks;
        target = g.DurationTicks > 0 ? Math.Clamp(target, 0, g.DurationTicks)
                                     : Math.Max(target, 0);

        Interlocked.Exchange(ref g.TargetTicks, target);
        Wake(g);
    }

    private static void Wake(FfRwGesture g)
    {
        // The pump may have already exited and cleaned up (e.g. the session refused
        // a seek), leaving detents to arrive against a disposed semaphore.
        try
        {
            if (g.Signal.CurrentCount == 0) g.Signal.Release(); // collapse bursts to one wake
        }
        catch (SemaphoreFullException) { }
        catch (ObjectDisposedException) { }
    }

    // The only awaiter of SeekToAsync, so at most one seek is ever in flight and
    // completions cannot arrive out of order.
    private async Task FfRwPumpAsync(FfRwGesture g)
    {
        long applied = long.MinValue;
        var ct = g.Cts.Token;
        try
        {
            while (true)
            {
                await g.Signal.WaitAsync(ct);

                while (true)
                {
                    if (ct.IsCancellationRequested) return;

                    // The track changed under us; seeking now would scrub the new one.
                    if (_media.GetSessionGeneration() != g.GenerationAtEntry)
                    {
                        g.SeekSupported = false;
                        return;
                    }

                    long target = Interlocked.Read(ref g.TargetTicks);
                    if (target == applied) break;   // caught up; wait for the next detent
                    applied = target;

                    bool ok;
                    try { ok = await _media.SeekToAsync(TimeSpan.FromTicks(target)); }
                    catch { ok = false; }

                    if (ct.IsCancellationRequested) return; // released and cancelled mid-seek
                    if (!ok) { g.SeekSupported = false; return; } // session cannot seek
                }

                if (g.StopRequested) return; // released: the final flick has landed
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            g.Cts.Dispose();
            g.Signal.Dispose();
        }
    }

    /// <param name="graceful">
    /// true on button release: let the pump apply the last target, then exit.
    /// false on disconnect/dispose: abandon immediately, issuing no further seek.
    /// </param>
    private void StopFfRw(bool graceful)
    {
        var g = Interlocked.Exchange(ref _ffRw, null);
        _ffRwSteps = 0;
        if (g == null) return;

        if (graceful)
        {
            g.StopRequested = true;
            Wake(g);
        }
        else
        {
            // The pump disposes its own CTS on exit, so it may already be gone.
            try { g.Cts.Cancel(); } catch (ObjectDisposedException) { }
        }
        // Never awaited — the HID thread must not block on a seek.
    }

    /// <summary>Where the user is seeking TO, so the LED and tray don't stutter on stale SMTC values.</summary>
    public float GetFfRwFraction()
    {
        var g = Volatile.Read(ref _ffRw);
        if (g != null && g.SeekSupported && g.DurationTicks > 0)
            return Math.Clamp(Interlocked.Read(ref g.TargetTicks) / (float)g.DurationTicks, 0f, 1f);

        return _media.GetPlaybackPosition();
    }

    // ── FF/RW LED tick ────────────────────────────────────────────────────────

    private void FfRwLedTick(object? _)
    {
        if (!_hid.IsConnected) return;
        WriteLed(GetFfRwFraction());
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
            WriteLed(_audio.GetLevel());
            return;
        }

        float peak = _config.LedBassOnly
            ? _audio.GetBassPeak()
            : _audio.GetPeakLevel();
        WriteLed(peak);
    }

    // ── Power management ──────────────────────────────────────────────────────

    public void Suspend()
    {
        _audioPulseTimer?.Dispose();
        _audioPulseTimer = null;
        _audio.StopCapture();
        _hid.Suspend();
    }

    public void Resume()
    {
        _hid.Resume();
        // Delay audio restart to allow WASAPI to reinitialize after sleep/hibernate
        _ = Task.Delay(ResumeCaptureDelayMs).ContinueWith(_ => ApplyAudioPulse());
    }

    public void Dispose()
    {
        StopFfRw(graceful: false);
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
