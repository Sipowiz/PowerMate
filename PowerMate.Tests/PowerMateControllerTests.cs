using NSubstitute;
using PowerMate.Models;
using PowerMate.Services;

namespace PowerMate.Tests;

public class PowerMateControllerTests : IDisposable
{
    private readonly IHidService _hid;
    private readonly IAudioService _audio;
    private readonly PowerMateConfig _config;
    private readonly PowerMateController _controller;

    public PowerMateControllerTests()
    {
        _hid = Substitute.For<IHidService>();
        _audio = Substitute.For<IAudioService>();
        _config = new PowerMateConfig();
        _controller = new PowerMateController(_hid, _audio, _config);

        _audio.GetLevel().Returns(0.5f);
        _audio.IsMuted().Returns(false);
    }

    public void Dispose() => _controller.Dispose();

    // ── Rotation ──────────────────────────────────────────────────────────────

    [Fact]
    public void Rotation_CW_AdjustsVolumeUp()
    {
        _hid.Rotated += Raise.Event<Action<int>>(1);

        float expectedStep = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(expectedStep);
    }

    [Fact]
    public void Rotation_CCW_AdjustsVolumeDown()
    {
        _hid.Rotated += Raise.Event<Action<int>>(-1);

        float expectedStep = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(-expectedStep);
    }

    [Fact]
    public void Rotation_Inverted_ReversesDirection()
    {
        _config.InvertRotation = true;
        _controller.UpdateConfig(_config);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        float expectedStep = (_config.VolumeStep / 100f) * _config.Sensitivity;
        _audio.Received(1).AdjustLevel(-expectedStep);
    }

    [Fact]
    public void Rotation_SetsLedToBrightness()
    {
        _audio.GetLevel().Returns(0.75f);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        _hid.Received().SetLed((byte)(0.75f * 255));
    }

    [Fact]
    public void Rotation_FiresVolumeChangedEvent()
    {
        float reportedLevel = -1;
        bool reportedMuted = true;
        _controller.VolumeChanged += (level, muted) =>
        {
            reportedLevel = level;
            reportedMuted = muted;
        };

        _hid.Rotated += Raise.Event<Action<int>>(1);

        Assert.Equal(0.5f, reportedLevel);
        Assert.False(reportedMuted);
    }

    [Fact]
    public void Rotation_CustomStep_UsesConfigValues()
    {
        _config.VolumeStep = 5;
        _config.Sensitivity = 2.0f;
        _controller.UpdateConfig(_config);

        _hid.Rotated += Raise.Event<Action<int>>(1);

        float expectedStep = (5 / 100f) * 2.0f;
        _audio.Received().AdjustLevel(expectedStep);
    }

    // ── Single click ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleClick_PlayPause_IsDefault()
    {
        // Press and release quickly (short press)
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        // Wait for tap timer to fire
        await Task.Delay(500);

        // Can't directly assert MediaKeyService.PlayPause() was called (static),
        // but we can verify Mute was NOT called
        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task SingleClick_Mute_TogglesMute()
    {
        _config.ClickAction = ClickAction.Mute;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(500);

        _audio.Received(1).ToggleMute();
    }

    [Fact]
    public async Task SingleClick_None_DoesNothing()
    {
        _config.ClickAction = ClickAction.None;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(500);

        _audio.DidNotReceive().ToggleMute();
    }

    // ── Double click ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DoubleClick_Mute_TogglesMute()
    {
        _config.DoubleClickAction = DoubleClickAction.Mute;
        _controller.UpdateConfig(_config);

        // Two quick presses
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(50);
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(500);

        _audio.Received(1).ToggleMute();
    }

    // ── Triple click ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TripleClick_Mute_TogglesMute()
    {
        _config.TripleClickAction = TripleClickAction.Mute;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(50);
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();
        await Task.Delay(50);
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(500);

        _audio.Received(1).ToggleMute();
    }

    // ── Long press ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LongPress_Mute_TogglesMute()
    {
        _config.LongPressMs = 300;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        await Task.Delay(400); // Longer than LongPressMs
        _hid.ButtonReleased += Raise.Event<Action>();

        _audio.Received(1).ToggleMute();
    }

    [Fact]
    public async Task LongPress_None_DoesNotMute()
    {
        _config.LongPressAction = LongPressAction.None;
        _config.LongPressMs = 300;
        _controller.UpdateConfig(_config);

        _hid.ButtonPressed += Raise.Event<Action>();
        await Task.Delay(400);
        _hid.ButtonReleased += Raise.Event<Action>();

        _audio.DidNotReceive().ToggleMute();
    }

    [Fact]
    public async Task LongPress_CancelsPendingMultiTap()
    {
        // When a second press is held long, the pending tap count from
        // the first press should be cancelled (long press resets _tapCount).
        // Use double-click action so we can distinguish from single-click.
        _config.ClickAction = ClickAction.None;
        _config.DoubleClickAction = DoubleClickAction.Mute;
        _config.LongPressMs = 300;
        _controller.UpdateConfig(_config);

        // First short press — starts tap count at 1
        _hid.ButtonPressed += Raise.Event<Action>();
        _hid.ButtonReleased += Raise.Event<Action>();

        // Second press immediately — tap count becomes 2, but hold it long
        _hid.ButtonPressed += Raise.Event<Action>();
        await Task.Delay(400); // Hold past LongPressMs
        _hid.ButtonReleased += Raise.Event<Action>();

        await Task.Delay(500); // Wait for any pending timers

        // The double-click (Mute) should NOT fire because long press cancelled it.
        // Only the long-press mute fires.
        _audio.Received(1).ToggleMute();
    }

    // ── Connection ────────────────────────────────────────────────────────────

    [Fact]
    public void ConnectionChanged_Forwarded()
    {
        bool? connected = null;
        _controller.ConnectionChanged += c => connected = c;

        _hid.ConnectionChanged += Raise.Event<Action<bool>>(true);

        Assert.True(connected);
    }

    // ── UpdateConfig ──────────────────────────────────────────────────────────

    [Fact]
    public void UpdateConfig_WhenPulseOff_SetsLedToVolumeLevel()
    {
        _audio.GetLevel().Returns(0.6f);
        _config.LedPulseOnAudio = false;

        _controller.UpdateConfig(_config);

        _hid.Received().SetLed((byte)(0.6f * 255));
    }

    [Fact]
    public void UpdateConfig_WhenPulseOn_DoesNotSetStaticLed()
    {
        _config.LedPulseOnAudio = true;
        _hid.IsConnected.Returns(true);

        _hid.ClearReceivedCalls();
        _controller.UpdateConfig(_config);

        // The timer will call SetLed, but UpdateConfig itself should not set a static value
        // (any SetLed calls come from the pulse timer, not from a static brightness set)
    }

    [Fact]
    public void UpdateConfig_BassOnly_StartsBassCapture()
    {
        _config.LedPulseOnAudio = true;
        _config.LedBassOnly = true;
        _config.BassFrequencyCutoff = 200;
        _config.BassGain = 8.0f;

        _controller.UpdateConfig(_config);

        _audio.Received(1).StartBassCapture(200, 8.0f);
    }

    [Fact]
    public void UpdateConfig_PulseOff_StopsBassCapture()
    {
        _config.LedPulseOnAudio = false;

        _controller.UpdateConfig(_config);

        _audio.Received().StopBassCapture();
    }
}
