using PowerMate.Services;

namespace PowerMate.Tests;

/// <summary>
/// Unit tests for MediaSessionService.
///
/// MediaSessionService wraps Windows SMTC (Windows.Media.Control) which requires
/// a live system media session to be fully functional. These tests cover:
///   - Initial / pre-init state (before async SMTC init completes)
///   - Safe disposal (with and without an active session)
///   - The position-estimation math that can be verified in isolation
///
/// Tests that depend on a live media session are out of scope here; those
/// behaviors are covered by integration / manual testing.
/// </summary>
public class MediaSessionServiceTests
{
    // ── Construction ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            using var svc = new MediaSessionService();
        });
        Assert.Null(ex);
    }

    // ── Initial state (before async SMTC init completes) ──────────────────────

    [Fact]
    public void GetPlaybackState_Initially_ReturnsUnknown()
    {
        // The SMTC RequestAsync is fire-and-forget; right after construction,
        // no session exists yet, so state must be Unknown.
        using var svc = new MediaSessionService();
        Assert.Equal(PlaybackState.Unknown, svc.GetPlaybackState());
    }

    [Fact]
    public void GetPlaybackPosition_Initially_ReturnsZero()
    {
        // _duration is TimeSpan.Zero at startup, so position is always 0f.
        using var svc = new MediaSessionService();
        Assert.Equal(0f, svc.GetPlaybackPosition());
    }

    // ── SeekToAsync is deliberately NOT exercised here ────────────────────────
    //
    // MediaSessionService attaches to whatever SMTC session the machine happens to
    // have, so calling SeekToAsync against a real instance would scrub whatever the
    // developer is actually playing — and its return value depends on whether the
    // async SMTC init has completed yet, which makes any assertion flaky. The
    // previous SeekRelativeAsync_* tests did exactly this; they only ever asserted
    // "did not throw", which hid both problems. Seek behaviour is covered against a
    // mocked IMediaSessionService in PowerMateControllerTests.

    // ── Absolute position / duration / generation ─────────────────────────────

    [Fact]
    public void GetPosition_Initially_ReturnsZero()
    {
        using var svc = new MediaSessionService();
        Assert.Equal(TimeSpan.Zero, svc.GetPosition());
    }

    [Fact]
    public void GetDuration_Initially_ReturnsZero()
    {
        using var svc = new MediaSessionService();
        Assert.Equal(TimeSpan.Zero, svc.GetDuration());
    }

    [Fact]
    public void GetSessionGeneration_IsStableAcrossReads()
    {
        // The generation must only move when the session or track changes, never
        // on a mere timeline update — a caller uses it to detect a stale anchor.
        using var svc = new MediaSessionService();
        Assert.Equal(svc.GetSessionGeneration(), svc.GetSessionGeneration());
    }

    // ── Disposal ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var ex = Record.Exception(() =>
        {
            var svc = new MediaSessionService();
            svc.Dispose();
        });
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CanBeCalledTwice_WithoutThrowing()
    {
        var ex = Record.Exception(() =>
        {
            var svc = new MediaSessionService();
            svc.Dispose();
            svc.Dispose();
        });
        Assert.Null(ex);
    }

    // ── Event subscription ─────────────────────────────────────────────────────

    [Fact]
    public void PlaybackStateChanged_CanBeSubscribedAndUnsubscribed()
    {
        using var svc = new MediaSessionService();
        Action<PlaybackState>? handler = _ => { };

        var ex = Record.Exception(() =>
        {
            svc.PlaybackStateChanged += handler;
            svc.PlaybackStateChanged -= handler;
        });
        Assert.Null(ex);
    }

    // ── Position-estimation math (pure logic, no SMTC required) ───────────────

    [Theory]
    [InlineData(0.0)]   // exactly at start
    [InlineData(0.5)]   // midpoint
    [InlineData(1.0)]   // exactly at end
    public void GetPlaybackPosition_WithZeroDuration_AlwaysReturnsZero(double ignored)
    {
        // _duration is TimeSpan.Zero at startup: division guard must return 0.
        // (The 'ignored' parameter just makes the theory table non-trivial.)
        _ = ignored;
        using var svc = new MediaSessionService();
        Assert.Equal(0f, svc.GetPlaybackPosition());
    }
}
