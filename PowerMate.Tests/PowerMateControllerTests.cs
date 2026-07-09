using NSubstitute;
using PowerMate.Models;
using PowerMate.Services;

namespace PowerMate.Tests;

/// <summary>
/// Unit tests for PowerMateController.
/// All hardware and audio calls go through NSubstitute mocks.
/// </summary>
public class PowerMateControllerTests : IDisposable
{
    private readonly IHidService  _hid;
    private readonly IAudioService _audio;
    private readonly IMediaSessionService _media;
    private readonly PowerMateConfig _config;
    private readonly PowerMateController _controller;

    // Short timing constants so tests run fast without sacrificing accuracy.
    private const int TapWindow  = 200;  // ms
    private const int LongPress  = 200;  // ms

    public PowerMateControllerTests()
    {
        _hid   = Substitute.For<IHidService>();
        _audio = Substitute.For<IAudioService>();
        _media = Substitute.For<IMediaSessionService>();
        _media.SeekToAsync(Arg.Any<TimeSpan>()).Returns(Task.FromResult(true));
        // GetPosition/GetDuration/GetSessionGeneration default to Zero/Zero/0,
        // i.e. an anchor at the very start of a track of unknown length.

        _config = new PowerMateConfig
        {
            TapWindowMs     = TapWindow,
            LongPressMs     = LongPress,
            FfRwThreshold   = 3,
            FfRwStepSeconds = 5,
        };
        _controller = new PowerMateController(_hid, _audio, _media, _config);
        _controller.IdleTimeoutMs = 300; // fast idle for timeout tests

        _audio.GetLevel().Returns(0.5f);
        _audio.IsMuted().Returns(false);
    }

    public void Dispose() => _controller.Dispose();

    // ══════════════════════════════════════════════════════════════════════════
    // Rotation — volume control
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Rotation_CW_AdjustsVolumeUp()
    {
        _hid.Rotated += Raise.Event<Action<int>>(1);

        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(step);
    }

    [Fact]
    public void Rotation_CCW_AdjustsVolumeDown()
    {
        _hid.Rotated += Raise.Event<Action<int>>(-1);

        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(-step);
    }

    [Fact]
    public void Rotation_Inverted_ReversesDirection()
    {
        _config.InvertRotation = true;
        _controller.UpdateConfig(_config);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(-step);
    }

    [Fact]
    public void Rotation_CustomStep_UsesConfigValues()
    {
        _config.VolumeStep  = 5;
        _config.Sensitivity = 2.0f;
        _controller.UpdateConfig(_config);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        _audio.Received(1).AdjustLevel((5 / 100f) * 2.0f);
    }

    [Fact]
    public void Rotation_SetsLedToCurrentVolume()
    {
        _audio.GetLevel().Returns(0.75f);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Received().SetLed((byte)(0.75f * 255));
    }

    [Fact]
    public void Rotation_FiresVolumeChangedEvent()
    {
        float level = -1; bool muted = true;
        _controller.VolumeChanged += (l, m) => { level = l; muted = m; };

        _hid.Rotated += Raise.Event<Action<int>>(1);

        Assert.Equal(0.5f, level);
        Assert.False(muted);
    }

    [Fact]
    public void MultipleRotations_EachAdjustsVolumeOnce()
    {
        float step = (_config.VolumeStep / 100f) * _config.Sensitivity;

        for (int i = 0; i < 4; i++)
            _hid.Rotated += Raise.Event<Action<int>>(1);

        _audio.Received(4).AdjustLevel(step);
    }

    [Fact]
    public void Rotation_RaisesVolumeModeEvent()
    {
        var modes = new List<InteractionMode>();
        _controller.InteractionModeChanged += modes.Add;

        _hid.Rotated += Raise.Event<Action<int>>(1);

        Assert.Contains(InteractionMode.Volume, modes);
    }

    [Fact]
    public void Rotation_InSameMode_DoesNotFireModeEventAgain()
    {
        var modes = new List<InteractionMode>();
        _controller.InteractionModeChanged += modes.Add;

        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);

        // Volume mode was entered once; subsequent rotations do not re-fire it.
        Assert.Equal(1, modes.Count(m => m == InteractionMode.Volume));
    }

    // ── Rotation suppressed during tap window ─────────────────────────────────

    [Fact]
    public async Task Rotation_DuringTapWindow_IsIgnored()
    {
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>(); // tap count now 1

        await Task.Delay(30); // still inside tap window
        _hid.Rotated += Raise.Event<Action<int>>(1);

        _audio.DidNotReceive().AdjustLevel(Arg.Any<float>());
    }

    [Fact]
    public async Task Rotation_AfterTapWindowExpired_WorksNormally()
    {
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(TapWindow + 100); // tap window has expired

        _audio.ClearReceivedCalls();
        _hid.Rotated += Raise.Event<Action<int>>(1);

        _audio.Received(1).AdjustLevel(Arg.Any<float>());
    }

    // ── Idle timeout → mode returns to Idle ──────────────────────────────────

    [Fact]
    public async Task IdleTimeout_AfterRotation_ResetsInteractionMode()
    {
        var modes = new List<InteractionMode>();
        _controller.InteractionModeChanged += modes.Add;

        _hid.Rotated += Raise.Event<Action<int>>(1); // → Volume

        await Task.Delay(_controller.IdleTimeoutMs + 100);

        Assert.Contains(InteractionMode.Idle, modes);
    }

    [Fact]
    public async Task IdleTimeout_WhenPulseOff_RestoresLedToVolume()
    {
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _audio.GetLevel().Returns(0.8f);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        _hid.ClearReceivedCalls();
        await Task.Delay(_controller.IdleTimeoutMs + 100);

        _hid.Received().SetLed((byte)(0.8f * 255));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // System volume changed (from outside)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SystemVolumeChange_FiresVolumeChangedEvent()
    {
        float level = -1; bool muted = true;
        _controller.VolumeChanged += (l, m) => { level = l; muted = m; };

        _audio.VolumeChanged += Raise.Event<Action<float, bool>>(0.9f, false);

        Assert.Equal(0.9f, level);
        Assert.False(muted);
    }

    [Fact]
    public void SystemVolumeChange_UpdatesLed_WhenPulseOff()
    {
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _hid.ClearReceivedCalls();

        _audio.VolumeChanged += Raise.Event<Action<float, bool>>(0.6f, false);

        _hid.Received().SetLed((byte)(0.6f * 255));
    }

    [Fact]
    public void SystemVolumeChange_DoesNotUpdateLed_WhenPulseOn()
    {
        _config.LedPulseOnAudio = true;
        _controller.UpdateConfig(_config);
        _hid.ClearReceivedCalls();

        _audio.VolumeChanged += Raise.Event<Action<float, bool>>(0.6f, false);

        // LED is managed by the pulse timer, not by this event.
        _hid.DidNotReceive().SetLed(Arg.Any<byte>());
    }

    [Fact]
    public void SystemVolumeChange_Suppressed_WhileSelfChanging()
    {
        // When _selfChanging is true (our own AdjustLevel triggered the notification),
        // the controller must not re-fire VolumeChanged (feedback-loop guard).
        int callCount = 0;
        _controller.VolumeChanged += (_, _) => callCount++;

        // Configure mock so that AdjustLevel synchronously fires the audio event
        // (simulating the OS callback arriving while _selfChanging is true).
        _audio.When(a => a.AdjustLevel(Arg.Any<float>()))
              .Do(_ => _audio.VolumeChanged += Raise.Event<Action<float, bool>>(0.5f, false));

        _hid.Rotated += Raise.Event<Action<int>>(1);

        // Exactly one VolumeChanged call: from our explicit code path, not the re-entrant one.
        Assert.Equal(1, callCount);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Button — hardcoded actions
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SinglePress_DoesNotToggleMute()
    {
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(TapWindow + 100);
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task DoublePress_DoesNotToggleMute()
    {
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(30);
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(TapWindow + 100);
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task TriplePress_DoesNotToggleMute()
    {
        for (int i = 0; i < 3; i++)
        {
            _hid.ButtonPressed  += Raise.Event<Action>();
            _hid.ButtonReleased += Raise.Event<Action>();
            if (i < 2) await Task.Delay(30);
        }
        await Task.Delay(TapWindow + 100);
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task LongPress_TogglesMute_BeforeRelease()
    {
        // Mute fires immediately when the threshold is crossed, not on release.
        _hid.ButtonPressed += Raise.Event<Action>();
        await Task.Delay(LongPress + 100);
        _audio.Received(1).ToggleMute(); // already fired, button still held
        _hid.ButtonReleased += Raise.Event<Action>();
        _audio.Received(1).ToggleMute(); // still exactly once after release
    }

    [Fact]
    public async Task ShortPress_JustUnderLongPressThreshold_DoesNotMute()
    {
        _hid.ButtonPressed  += Raise.Event<Action>();
        await Task.Delay(LongPress / 2);  // well under — timer not yet fired
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(TapWindow + 100);
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task LongPress_CancelsPendingTap()
    {
        _hid.ButtonPressed  += Raise.Event<Action>(); // tap 1
        _hid.ButtonReleased += Raise.Event<Action>();

        _hid.ButtonPressed  += Raise.Event<Action>(); // held long → fires mute immediately
        await Task.Delay(LongPress + 100);
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(TapWindow + 100);
        _audio.Received(1).ToggleMute(); // exactly once, tap from press 1 was cancelled
    }

    [Fact]
    public void ButtonPress_RaisesButtonModeEvent()
    {
        InteractionMode? mode = null;
        _controller.InteractionModeChanged += m => mode = m;
        _hid.ButtonPressed += Raise.Event<Action>();
        Assert.Equal(InteractionMode.Button, mode);
    }

    [Fact]
    public async Task TapThenLongPress_OnlyMutes_NotDoubleAction()
    {
        // Single tap followed by a long press within the tap window.
        // The stale tap timer must not fire while the button is held; only mute should execute.
        _hid.ButtonPressed  += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>(); // tap 1 pending

        // Re-press immediately (within tap window) and hold for long press.
        _hid.ButtonPressed  += Raise.Event<Action>();
        await Task.Delay(LongPress + 100); // long press timer fires → mute
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(TapWindow + 100); // wait out any stale timers

        // Mute executed exactly once. (PlayPause goes via SendInput and can't be intercepted
        // in unit tests, but the tap timer guard ensures it is not called.)
        _audio.Received(1).ToggleMute();
    }

    [Fact]
    public void ButtonRelease_WithoutPriorPress_IsIgnored()
    {
        // Should not throw or fire any events.
        var ex = Record.Exception(() => _hid.ButtonReleased += Raise.Event<Action>());
        Assert.Null(ex);
        _audio.DidNotReceive().ToggleMute();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // FF/RW — button held + rotation
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FfRw_BelowThreshold_DoesNotSeek()
    {
        _config.FfRwThreshold = 3;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1); // 2 < threshold
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(50);

        await _media.DidNotReceive().SeekToAsync(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task FfRw_AtThreshold_Seeks()
    {
        _config.FfRwThreshold   = 3;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        for (int i = 0; i < 3; i++)
            _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);

        // Anchor is 0; the detent that crossed the threshold is the first to seek.
        await _media.Received().SeekToAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FfRw_CcwRotation_SeeksBackwardFromAnchor()
    {
        _config.FfRwThreshold   = 2;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);
        // Anchor away from 0 so a backward seek is not clamped at the track start.
        _media.GetPosition().Returns(TimeSpan.FromMinutes(5));
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(-1);
        _hid.Rotated += Raise.Event<Action<int>>(-1);
        await Task.Delay(50);

        await _media.Received().SeekToAsync(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FfRw_FastSpin_CoalescesToLatestCumulativeTarget()
    {
        // The bug: one SMTC seek per detent, each computed from a stale position.
        // Now detents accumulate into one absolute target against a frozen anchor.
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));

        _hid.ButtonPressed += Raise.Event<Action>();
        for (int i = 0; i < 5; i++)          // 5 detents → cumulative +25s
            _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(100);               // let the pump drain

        await _media.Received().SeekToAsync(TimeSpan.FromSeconds(25));

        int seekCalls = _media.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IMediaSessionService.SeekToAsync));
        Assert.True(seekCalls < 5, $"expected coalesced seeks, got {seekCalls} for 5 detents");
    }

    [Fact]
    public async Task FfRw_SeekTargetIsAbsolute_NotRelativeToLiveSmtcPosition()
    {
        // Regression for #4: SMTC lags after a seek. Even if it keeps reporting the
        // pre-gesture position, the target must be anchor + cumulative offset.
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 10;
        _controller.UpdateConfig(_config);
        _media.GetPosition().Returns(TimeSpan.FromMinutes(1));   // read once, at entry
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);

        // 1:00 + 20s, not 1:00 + 10s twice.
        await _media.Received().SeekToAsync(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task FfRw_SeekTargetIsClampedToTrackBounds()
    {
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 30;
        _controller.UpdateConfig(_config);
        _media.GetPosition().Returns(TimeSpan.FromSeconds(10));
        _media.GetDuration().Returns(TimeSpan.FromSeconds(60));

        _hid.ButtonPressed += Raise.Event<Action>();
        for (int i = 0; i < 5; i++)          // 10s + 150s → far past the end
            _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(100);

        await _media.Received().SeekToAsync(TimeSpan.FromSeconds(60));
        await _media.DidNotReceive().SeekToAsync(Arg.Is<TimeSpan>(t => t > TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task FfRw_WhenSessionCannotSeek_StopsSeeking()
    {
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));
        _media.SeekToAsync(Arg.Any<TimeSpan>()).Returns(Task.FromResult(false));

        _hid.ButtonPressed += Raise.Event<Action>();
        for (int i = 0; i < 6; i++)
            _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(100);

        // The pump gives up after the first refusal rather than hammering the session.
        int seekCalls = _media.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IMediaSessionService.SeekToAsync));
        Assert.Equal(1, seekCalls);
    }

    [Fact]
    public async Task FfRw_Disconnect_DoesNotSeekAfterwards()
    {
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);                                       // pump applies the first target

        _hid.ConnectionChanged += Raise.Event<Action<bool>>(false); // unplugged mid-gesture
        _media.ClearReceivedCalls();

        _hid.Rotated += Raise.Event<Action<int>>(1);                // stray detent after the drop
        await Task.Delay(100);

        await _media.DidNotReceive().SeekToAsync(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task FfRw_MixedDirections_BothCountTowardThreshold()
    {
        // CW then CCW — total absolute steps = 2 which meets threshold = 2.
        _config.FfRwThreshold   = 2;
        _config.FfRwStepSeconds = 5;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);  // step 1
        _hid.Rotated += Raise.Event<Action<int>>(-1); // step 2 — enters FfRw
        await Task.Delay(50);

        // The second step triggered FfRw; seek was called at least once.
        await _media.Received().SeekToAsync(Arg.Any<TimeSpan>());
    }

    [Fact]
    public async Task FfRw_InvertedRotation_SeeksInOppositeDirection()
    {
        _config.FfRwThreshold   = 2;
        _config.FfRwStepSeconds = 5;
        _config.InvertRotation  = true;
        _controller.UpdateConfig(_config);
        // Anchor away from 0 so the inverted (backward) seek is not clamped.
        _media.GetPosition().Returns(TimeSpan.FromMinutes(5));
        _media.GetDuration().Returns(TimeSpan.FromMinutes(10));

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);

        await _media.Received().SeekToAsync(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FfRw_RaisesInteractionModeFfRwEvent()
    {
        _config.FfRwThreshold = 2;
        _controller.UpdateConfig(_config);

        var modes = new List<InteractionMode>();
        _controller.InteractionModeChanged += modes.Add;

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);

        Assert.Contains(InteractionMode.FfRw, modes);
    }

    [Fact]
    public async Task FfRw_Release_DoesNotFireTapAction()
    {
        _config.FfRwThreshold = 2;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(TapWindow + 100);

        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task FfRw_AfterRelease_VolumeRotationResumesNormally()
    {
        _config.FfRwThreshold = 2;
        _controller.UpdateConfig(_config);
        _controller.FfRwReleaseGuardMs = 0;

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(50); // let the release guard timer expire

        _audio.ClearReceivedCalls();
        _hid.Rotated += Raise.Event<Action<int>>(1); // normal rotation, button not held

        _audio.Received(1).AdjustLevel(Arg.Any<float>());
    }

    [Fact]
    public void FfRw_ReleaseGuard_BlocksVolumeImmediatelyAfterRelease()
    {
        _config.FfRwThreshold = 2;
        _controller.UpdateConfig(_config);
        // Leave FfRwReleaseGuardMs at default (500) — guard should be active

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.Rotated += Raise.Event<Action<int>>(1);
        _hid.ButtonReleased += Raise.Event<Action>();

        _audio.ClearReceivedCalls();
        _hid.Rotated += Raise.Event<Action<int>>(1); // during guard window

        _audio.DidNotReceive().AdjustLevel(Arg.Any<float>());
    }

    [Fact]
    public void FfRw_RotationWhileHeld_DoesNotAdjustVolume()
    {
        // Any rotation while button is held must NOT touch volume.
        _config.FfRwThreshold = 10; // high threshold → stays below FfRw
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        for (int i = 0; i < 5; i++)
            _hid.Rotated += Raise.Event<Action<int>>(1);

        _audio.DidNotReceive().AdjustLevel(Arg.Any<float>());
    }

    [Fact]
    public async Task FfRw_ThresholdOne_SingleStepSeeks()
    {
        _config.FfRwThreshold   = 1;
        _config.FfRwStepSeconds = 3;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.Rotated += Raise.Event<Action<int>>(1);
        await Task.Delay(50);

        await _media.Received(1).SeekToAsync(TimeSpan.FromSeconds(3));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Connection
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsConnected_ReflectsHidService()
    {
        _hid.IsConnected.Returns(true);
        Assert.True(_controller.IsConnected);

        _hid.IsConnected.Returns(false);
        Assert.False(_controller.IsConnected);
    }

    [Fact]
    public void ConnectionChanged_True_IsForwarded()
    {
        bool? connected = null;
        _controller.ConnectionChanged += c => connected = c;
        _hid.ConnectionChanged += Raise.Event<Action<bool>>(true);
        Assert.True(connected);
    }

    [Fact]
    public void ConnectionChanged_False_IsForwarded()
    {
        bool? connected = null;
        _controller.ConnectionChanged += c => connected = c;
        _hid.ConnectionChanged += Raise.Event<Action<bool>>(false);
        Assert.False(connected);
    }

    [Fact]
    public async Task Disconnect_WhileButtonHeld_CancelsPendingLongPress()
    {
        // Unplugging mid-press must not leave a long-press timer armed to fire mute.
        _hid.ButtonPressed     += Raise.Event<Action>();
        _hid.ConnectionChanged += Raise.Event<Action<bool>>(false);

        await Task.Delay(LongPress + 100);

        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task Disconnect_WhileButtonHeld_ReleaseAfterwardIsIgnored()
    {
        // A release arriving after the reset must not be counted as a tap, which
        // would make the user's next single click read as a double tap.
        _hid.ButtonPressed     += Raise.Event<Action>();
        _hid.ConnectionChanged += Raise.Event<Action<bool>>(false);

        var ex = Record.Exception(() => _hid.ButtonReleased += Raise.Event<Action>());
        Assert.Null(ex);

        await Task.Delay(TapWindow + 100);
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public void Disconnect_ReturnsInteractionModeToIdle()
    {
        var modes = new List<InteractionMode>();
        _hid.ButtonPressed += Raise.Event<Action>();   // → Button
        _controller.InteractionModeChanged += modes.Add;

        _hid.ConnectionChanged += Raise.Event<Action<bool>>(false);

        Assert.Contains(InteractionMode.Idle, modes);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // LED master brightness
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(255, 255)] // full brightness → full range
    [InlineData(128, 128)] // half brightness halves the LED output
    [InlineData(0,     0)] // zero brightness → LED off
    public void LedBrightness_ScalesLedOutput(int brightness, byte expected)
    {
        _audio.GetLevel().Returns(1.0f);
        _config.LedBrightness   = brightness;
        _config.LedPulseOnAudio = false;
        _hid.ClearReceivedCalls();

        _controller.UpdateConfig(_config);

        _hid.Received().SetLed(expected);
    }

    [Fact]
    public void LedBrightness_ScalesRotationLed()
    {
        _audio.GetLevel().Returns(0.5f);
        _config.LedBrightness = 128;
        _controller.UpdateConfig(_config);
        _hid.ClearReceivedCalls();

        _hid.Rotated += Raise.Event<Action<int>>(1);

        // 0.5 volume × (128/255) master → 64
        _hid.Received().SetLed(64);
    }

    [Fact]
    public async Task LedBrightness_ScalesAudioPulsePeak()
    {
        _hid.IsConnected.Returns(true);
        _audio.GetPeakLevel().Returns(1.0f);
        _media.GetPlaybackState().Returns(PlaybackState.Playing);
        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = false;
        _config.LedBrightness   = 128;

        _controller.UpdateConfig(_config);
        await Task.Delay(100); // let the pulse timer tick

        _hid.Received().SetLed(128);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // UpdateConfig
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateConfig_PulseOff_SetsLedToCurrentVolume()
    {
        _audio.GetLevel().Returns(0.6f);
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _hid.Received().SetLed((byte)(0.6f * 255));
    }

    [Fact]
    public void UpdateConfig_BassOnly_StartsBassCapture()
    {
        _config.LedPulseOnAudio     = true;
        _config.LedBassOnly         = true;
        _config.BassFrequencyCutoff = 200;
        _config.BassGain            = 8.0f;
        _controller.UpdateConfig(_config);
        _audio.Received(1).StartBassCapture(200, 8.0f);
    }

    [Fact]
    public void UpdateConfig_PulseOff_StopsCapture()
    {
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _audio.Received().StopCapture();
    }

    [Fact]
    public void UpdateConfig_PulseOn_NotBassOnly_StartsPeakCapture()
    {
        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = false;
        _controller.UpdateConfig(_config);
        _audio.Received().StartPeakCapture();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Audio-pulse LED: playback-state fallback
    // ══════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(PlaybackState.Paused)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Unknown)]
    public async Task AudioPulse_WhenNotPlaying_ShowsVolumeLed(PlaybackState state)
    {
        _hid.IsConnected.Returns(true);
        _audio.GetLevel().Returns(0.3f);
        _audio.GetPeakLevel().Returns(0.9f); // different value — must NOT appear
        _media.GetPlaybackState().Returns(state);

        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = false;
        _controller.UpdateConfig(_config);

        await Task.Delay(100);

        _hid.Received().SetLed((byte)(0.3f * 255));
        _hid.DidNotReceive().SetLed((byte)(0.9f * 255));
    }

    [Fact]
    public async Task AudioPulse_WhenPlaying_ShowsPeakLed()
    {
        _hid.IsConnected.Returns(true);
        _audio.GetLevel().Returns(0.3f);     // different value — must NOT appear
        _audio.GetPeakLevel().Returns(0.9f);
        _media.GetPlaybackState().Returns(PlaybackState.Playing);

        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = false;
        _controller.UpdateConfig(_config);

        await Task.Delay(100);

        _hid.Received().SetLed((byte)(0.9f * 255));
        _hid.DidNotReceive().SetLed((byte)(0.3f * 255));
    }

    [Fact]
    public async Task AudioPulse_WhenBassOnly_WhenPlaying_ShowsBassPeakLed()
    {
        _hid.IsConnected.Returns(true);
        _audio.GetLevel().Returns(0.3f);
        _audio.GetBassPeak().Returns(0.8f);
        _media.GetPlaybackState().Returns(PlaybackState.Playing);

        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = true;
        _controller.UpdateConfig(_config);

        await Task.Delay(100);

        _hid.Received().SetLed((byte)(0.8f * 255));
        _hid.DidNotReceive().SetLed((byte)(0.3f * 255));
    }

    [Fact]
    public async Task AudioPulse_WhenBassOnly_WhenPaused_ShowsVolumeLed()
    {
        _hid.IsConnected.Returns(true);
        _audio.GetLevel().Returns(0.3f);
        _audio.GetBassPeak().Returns(0.8f);  // must NOT appear
        _media.GetPlaybackState().Returns(PlaybackState.Paused);

        _config.LedPulseOnAudio = true;
        _config.LedBassOnly     = true;
        _controller.UpdateConfig(_config);

        await Task.Delay(100);

        _hid.Received().SetLed((byte)(0.3f * 255));
        _hid.DidNotReceive().SetLed((byte)(0.8f * 255));
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Suspend / Resume (power management)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Suspend_StopsAudioCapture()
    {
        _controller.Suspend();
        _audio.Received(1).StopCapture();
    }

    [Fact]
    public void Suspend_CallsHidSuspend()
    {
        _controller.Suspend();
        _hid.Received(1).Suspend();
    }

    [Fact]
    public void Resume_CallsHidResume()
    {
        _controller.Resume();
        _hid.Received(1).Resume();
    }

    [Fact]
    public void Suspend_WhenPulseOn_StopsCapture()
    {
        _config.LedPulseOnAudio = true;
        _controller.UpdateConfig(_config);
        _audio.ClearReceivedCalls();

        _controller.Suspend();

        _audio.Received(1).StopCapture();
    }

    [Fact]
    public async Task Resume_WhenPulseOn_RestartsCapture()
    {
        _config.LedPulseOnAudio = true;
        _controller.UpdateConfig(_config);
        _controller.ResumeCaptureDelayMs = 0;
        _audio.ClearReceivedCalls();

        _controller.Resume();
        await Task.Delay(100);

        _audio.Received().StartPeakCapture();
    }

    [Fact]
    public async Task Resume_WhenBassOnly_RestartsBassCapture()
    {
        _config.LedPulseOnAudio     = true;
        _config.LedBassOnly         = true;
        _config.BassFrequencyCutoff = 200;
        _config.BassGain            = 6.0f;
        _controller.UpdateConfig(_config);
        _controller.ResumeCaptureDelayMs = 0;
        _audio.ClearReceivedCalls();

        _controller.Resume();
        await Task.Delay(100);

        _audio.Received().StartBassCapture(200, 6.0f);
    }

    [Fact]
    public async Task Resume_WhenPulseOff_DoesNotRestartCapture()
    {
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _controller.ResumeCaptureDelayMs = 0;
        _audio.ClearReceivedCalls();

        _controller.Resume();
        await Task.Delay(100);

        _audio.DidNotReceive().StartPeakCapture();
        _audio.DidNotReceive().StartBassCapture(Arg.Any<int>(), Arg.Any<float>());
    }

    [Fact]
    public async Task SuspendThenResume_WithPulseOn_RestartsCapture()
    {
        _config.LedPulseOnAudio = true;
        _controller.UpdateConfig(_config);
        _controller.ResumeCaptureDelayMs = 0;

        _controller.Suspend();
        _audio.ClearReceivedCalls();

        _controller.Resume();
        await Task.Delay(100);

        _audio.Received().StartPeakCapture();
    }

    [Fact]
    public async Task SuspendThenResume_WithPulseOff_DoesNotRestartCapture()
    {
        _config.LedPulseOnAudio = false;
        _controller.UpdateConfig(_config);
        _controller.ResumeCaptureDelayMs = 0;

        _controller.Suspend();
        _audio.ClearReceivedCalls();

        _controller.Resume();
        await Task.Delay(100);

        _audio.DidNotReceive().StartPeakCapture();
        _audio.DidNotReceive().StartBassCapture(Arg.Any<int>(), Arg.Any<float>());
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Dispose / lifecycle
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var ex = Record.Exception(() => _controller.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DisposesHid()
    {
        _controller.Dispose();
        _hid.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_DisposesAudio()
    {
        _controller.Dispose();
        _audio.Received(1).Dispose();
    }

    [Fact]
    public void Dispose_DisposesMedia()
    {
        _controller.Dispose();
        _media.Received(1).Dispose();
    }
}
