using PowerMate.Services;
using PowerMate.WinUI;

namespace PowerMate.Tests;

/// <summary>
/// Tests for TrayIconRenderer.Render().
/// The renderer uses GDI+ and user32 HICONs — all Windows-only, which matches
/// the test project's net10.0-windows10.0.19041.0 target framework.
///
/// Note: TrayIconRenderer._lastHIcon is a static field that manages HICON lifetime.
/// Each Render() call destroys the previous HICON. Tests that use 'using var icon'
/// will dispose the Icon wrapper object, but the HICON itself is managed by the
/// renderer's static field.
/// </summary>
public class TrayIconRendererTests
{
    // ── Basic: non-null icon returned for all combinations ────────────────────

    [Theory]
    [InlineData(0.0f,  false, PlaybackState.Unknown,  false)]
    [InlineData(0.5f,  false, PlaybackState.Playing,  false)]
    [InlineData(1.0f,  false, PlaybackState.Paused,   false)]
    [InlineData(0.3f,  false, PlaybackState.Stopped,  false)]
    [InlineData(0.5f,  true,  PlaybackState.Playing,  false)]  // muted
    [InlineData(0.5f,  false, PlaybackState.Playing,  true)]   // interacting
    [InlineData(0.5f,  true,  PlaybackState.Paused,   true)]   // muted + interacting
    [InlineData(0.0f,  true,  PlaybackState.Unknown,  true)]   // muted + interacting + zero volume
    public void Render_ReturnsNonNull(
        float displayValue, bool muted, PlaybackState state, bool interacting)
    {
        using var icon = TrayIconRenderer.Render(displayValue, muted, state, interacting);
        Assert.NotNull(icon);
    }

    [Theory]
    [InlineData(0.0f,  false, PlaybackState.Unknown,  false)]
    [InlineData(0.5f,  false, PlaybackState.Playing,  false)]
    [InlineData(1.0f,  false, PlaybackState.Paused,   false)]
    [InlineData(0.5f,  true,  PlaybackState.Stopped,  false)]
    [InlineData(0.5f,  false, PlaybackState.Unknown,  true)]
    public void Render_ReturnsValidHandle(
        float displayValue, bool muted, PlaybackState state, bool interacting)
    {
        using var icon = TrayIconRenderer.Render(displayValue, muted, state, interacting);
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
    }

    // ── All PlaybackState enum values are handled ──────────────────────────────

    [Theory]
    [InlineData(PlaybackState.Unknown)]
    [InlineData(PlaybackState.Playing)]
    [InlineData(PlaybackState.Paused)]
    [InlineData(PlaybackState.Stopped)]
    public void Render_AllPlaybackStates_DoNotThrow(PlaybackState state)
    {
        var ex = Record.Exception(() =>
            TrayIconRenderer.Render(0.5f, false, state, false));
        Assert.Null(ex);
    }

    // ── Volume (displayValue) boundary values ──────────────────────────────────

    [Theory]
    [InlineData(0.0f)]      // zero — arc not drawn (threshold is 0.001)
    [InlineData(0.001f)]    // just at the arc threshold
    [InlineData(0.0011f)]   // just above the arc threshold
    [InlineData(0.5f)]
    [InlineData(1.0f)]      // full arc (360°)
    public void Render_DisplayValueBoundaries_DoNotThrow(float displayValue)
    {
        var ex = Record.Exception(() =>
            TrayIconRenderer.Render(displayValue, false));
        Assert.Null(ex);
    }

    // ── Ring color modes: muted / idle / interacting ───────────────────────────

    [Fact]
    public void Render_Muted_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            TrayIconRenderer.Render(0.5f, muted: true));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_Interacting_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            TrayIconRenderer.Render(0.5f, false, PlaybackState.Playing, interacting: true));
        Assert.Null(ex);
    }

    [Fact]
    public void Render_Idle_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
            TrayIconRenderer.Render(0.5f, false, PlaybackState.Paused, interacting: false));
        Assert.Null(ex);
    }

    // ── Sequential calls — no GDI resource crash ───────────────────────────────

    [Fact]
    public void Render_CalledRepeatedly_DoesNotThrow()
    {
        // Each call destroys the previous HICON and creates a new one.
        var ex = Record.Exception(() =>
        {
            for (int i = 0; i < 10; i++)
            {
                // Discard the return value; _lastHIcon manages cleanup.
                _ = TrayIconRenderer.Render(
                    i / 9f,
                    muted: i % 3 == 0,
                    (PlaybackState)(i % 4),
                    interacting: i % 2 == 0);
            }
        });
        Assert.Null(ex);
    }

    // ── Default optional parameters ────────────────────────────────────────────

    [Fact]
    public void Render_WithOnlyRequiredParams_UsesDefaults()
    {
        // PlaybackState defaults to Unknown, interacting defaults to false.
        var ex = Record.Exception(() =>
        {
            using var icon = TrayIconRenderer.Render(0.5f, false);
        });
        Assert.Null(ex);
    }
}
