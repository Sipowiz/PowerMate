using PowerMate.Services;

namespace PowerMate.Tests;

/// <summary>
/// Report-decoding tests for HidService. The byte patterns below were captured
/// from a real Griffin PowerMate (VID 0x077D / PID 0x0410, 7-byte input report).
/// </summary>
public class HidServiceTests
{
    // buf[0] = report id, buf[1] = button, buf[2] = signed rotation delta
    private static byte[] Report(byte button, byte rotation) =>
        [0x00, button, rotation, 0x00, 0x80, 0x10, 0x00];

    private static List<int> CaptureRotations(byte rotation)
    {
        using var hid = new HidService();
        var steps = new List<int>();
        hid.Rotated += steps.Add;
        hid.ProcessReport(Report(0, rotation));
        return steps;
    }

    // ── Rotation: byte 2 is a signed delta, not a direction flag ───────────────

    [Fact]
    public void Rotation_SingleStepCw_EmitsOneStep() =>
        Assert.Equal([1], CaptureRotations(0x01));

    [Fact]
    public void Rotation_SingleStepCcw_EmitsOneStep() =>
        Assert.Equal([-1], CaptureRotations(0xFF));

    [Fact]
    public void Rotation_Idle_EmitsNothing() =>
        Assert.Empty(CaptureRotations(0x00));

    // A fast spin batches several detents into one report. Before this was fixed,
    // every one of these reports was silently discarded.
    [Theory]
    [InlineData(0x02, 2)]   // +2
    [InlineData(0x03, 3)]   // +3
    [InlineData(0x04, 4)]   // +4
    public void Rotation_FastCw_EmitsOneStepPerDetent(byte raw, int expected)
    {
        var steps = CaptureRotations(raw);
        Assert.Equal(expected, steps.Count);
        Assert.All(steps, s => Assert.Equal(1, s));
    }

    [Theory]
    [InlineData(0xFE, 2)]   // -2
    [InlineData(0xFD, 3)]   // -3
    [InlineData(0xFC, 4)]   // -4
    public void Rotation_FastCcw_EmitsOneStepPerDetent(byte raw, int expected)
    {
        var steps = CaptureRotations(raw);
        Assert.Equal(expected, steps.Count);
        Assert.All(steps, s => Assert.Equal(-1, s));
    }

    // ── Button edges ──────────────────────────────────────────────────────────

    [Fact]
    public void Button_PressThenRelease_FiresOneEdgeEach()
    {
        using var hid = new HidService();
        int pressed = 0, released = 0;
        hid.ButtonPressed  += () => pressed++;
        hid.ButtonReleased += () => released++;

        hid.ProcessReport(Report(1, 0));
        hid.ProcessReport(Report(1, 0)); // repeat report — no new edge
        hid.ProcessReport(Report(0, 0));

        Assert.Equal(1, pressed);
        Assert.Equal(1, released);
    }

    [Fact]
    public void Button_HeldDuringDisconnect_DoesNotFirePhantomReleaseOnReconnect()
    {
        using var hid = new HidService();
        int released = 0;
        hid.ButtonReleased += () => released++;

        hid.ProcessReport(Report(1, 0)); // pressed…
        hid.Disconnect();                // …device unplugged while held

        // First report after reconnecting reports the button as up. That is not a
        // release edge — the press it would pair with never reached us.
        hid.ProcessReport(Report(0, 0));

        Assert.Equal(0, released);
    }

    [Fact]
    public void Rotation_WithButtonHeld_StillEmitsEveryDetent()
    {
        using var hid = new HidService();
        var steps = new List<int>();
        hid.Rotated += steps.Add;

        hid.ProcessReport(Report(1, 0xFD)); // held + 3 detents CCW

        Assert.Equal([-1, -1, -1], steps);
    }
}
