namespace PowerMate.Tests;

public class MauiProgramTests : IDisposable
{
    private readonly string _tempDir;

    public MauiProgramTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"powermate_crash_test_{Guid.NewGuid():N}");
        MauiProgram.TestCrashLogPath = Path.Combine(_tempDir, "crash.log");
    }

    public void Dispose()
    {
        MauiProgram.TestCrashLogPath = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string LogPath => MauiProgram.TestCrashLogPath!;

    // ── File creation ─────────────────────────────────────────────────────────

    [Fact]
    public void WriteCrashLog_CreatesFileAndDirectory()
    {
        MauiProgram.WriteCrashLog(new Exception("test"), "Test");
        Assert.True(File.Exists(LogPath));
    }

    // ── Content ───────────────────────────────────────────────────────────────

    [Fact]
    public void WriteCrashLog_ContainsExceptionMessage()
    {
        MauiProgram.WriteCrashLog(new Exception("boom!"), "Test");
        Assert.Contains("boom!", File.ReadAllText(LogPath));
    }

    [Fact]
    public void WriteCrashLog_ContainsSource()
    {
        MauiProgram.WriteCrashLog(new Exception("x"), "MySource");
        Assert.Contains("MySource", File.ReadAllText(LogPath));
    }

    [Fact]
    public void WriteCrashLog_ContainsTimestamp()
    {
        MauiProgram.WriteCrashLog(new Exception("x"), "Test");
        // Timestamp format is yyyy-MM-dd; check the year at minimum.
        Assert.Contains(DateTime.Now.Year.ToString(), File.ReadAllText(LogPath));
    }

    // ── Append behaviour ──────────────────────────────────────────────────────

    [Fact]
    public void WriteCrashLog_SecondCall_AppendsToPreviousEntry()
    {
        MauiProgram.WriteCrashLog(new Exception("first"), "A");
        MauiProgram.WriteCrashLog(new Exception("second"), "B");
        var content = File.ReadAllText(LogPath);
        Assert.Contains("first", content);
        Assert.Contains("second", content);
    }

    // ── Null exception ────────────────────────────────────────────────────────

    [Fact]
    public void WriteCrashLog_NullException_DoesNotThrow()
    {
        var ex = Record.Exception(() => MauiProgram.WriteCrashLog(null, "Test"));
        Assert.Null(ex);
    }

    [Fact]
    public void WriteCrashLog_NullException_StillWritesSourceAndTimestamp()
    {
        MauiProgram.WriteCrashLog(null, "NullSource");
        var content = File.ReadAllText(LogPath);
        Assert.Contains("NullSource", content);
        Assert.Contains(DateTime.Now.Year.ToString(), content);
    }
}
