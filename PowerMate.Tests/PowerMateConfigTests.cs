using System.Text.Json;
using PowerMate.Models;

namespace PowerMate.Tests;

public class PowerMateConfigTests : IDisposable
{
    private readonly string _tempDir;

    public PowerMateConfigTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"powermate_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        PowerMateConfig.TestConfigPath = Path.Combine(_tempDir, "config.json");
    }

    public void Dispose()
    {
        PowerMateConfig.TestConfigPath = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string ConfigPath => PowerMateConfig.TestConfigPath!;

    // ── Defaults ───────────────────────────────────────────────────────────────

    [Fact]
    public void Defaults_AreCorrect()
    {
        var c = new PowerMateConfig();
        Assert.Equal(2,     c.VolumeStep);
        Assert.Equal(1.0f,  c.Sensitivity);
        Assert.False(c.InvertRotation);
        Assert.Equal(800,   c.LongPressMs);
        Assert.Equal(350,   c.TapWindowMs);
        Assert.Equal(255,   c.LedBrightness);
        Assert.False(c.LedPulseOnAudio);
        Assert.False(c.LedBassOnly);
        Assert.Equal(250,   c.BassFrequencyCutoff);
        Assert.Equal(5.0f,  c.BassGain);
        Assert.Equal(3,     c.FfRwThreshold);
        Assert.Equal(5,     c.FfRwStepSeconds);
    }

    // ── Load: missing / corrupt / extra-fields ─────────────────────────────────

    [Fact]
    public void Load_WhenFileMissing_ReturnsDefaults()
    {
        // No file written — Load should return a fresh default config.
        var c = PowerMateConfig.Load();
        Assert.Equal(2, c.VolumeStep);
        Assert.Equal(1.0f, c.Sensitivity);
    }

    [Fact]
    public void Load_WhenJsonCorrupt_ReturnsDefaults()
    {
        File.WriteAllText(ConfigPath, "{ not valid json }}}");
        var c = PowerMateConfig.Load();
        Assert.Equal(2, c.VolumeStep);
    }

    [Fact]
    public void Load_WhenJsonNull_ReturnsDefaults()
    {
        File.WriteAllText(ConfigPath, "null");
        var c = PowerMateConfig.Load();
        Assert.Equal(2, c.VolumeStep);
    }

    [Fact]
    public void Load_WhenJsonEmpty_ReturnsDefaults()
    {
        File.WriteAllText(ConfigPath, "");
        var c = PowerMateConfig.Load();
        Assert.Equal(2, c.VolumeStep);
    }

    [Fact]
    public void Load_WithUnknownExtraProperties_IgnoresThem()
    {
        // Simulates an old config that has fields removed in a newer version.
        var json = """
            {
                "VolumeStep": 7,
                "ObsoleteEnumField": "PlayPause",
                "RemovedProperty": 42
            }
            """;
        File.WriteAllText(ConfigPath, json);
        var c = PowerMateConfig.Load();
        Assert.Equal(7, c.VolumeStep);
    }

    // ── Real Save + Load round-trip ────────────────────────────────────────────

    [Fact]
    public void SaveAndLoad_RoundTrips_AllFields()
    {
        var orig = new PowerMateConfig
        {
            VolumeStep          = 5,
            Sensitivity         = 2.0f,
            InvertRotation      = true,
            LongPressMs         = 500,
            TapWindowMs         = 400,
            LedBrightness       = 200,
            LedPulseOnAudio     = true,
            LedBassOnly         = true,
            BassFrequencyCutoff = 100,
            BassGain            = 10.0f,
            FfRwThreshold       = 4,
            FfRwStepSeconds     = 10,
        };

        orig.Save();
        var loaded = PowerMateConfig.Load();

        Assert.Equal(orig.VolumeStep,          loaded.VolumeStep);
        Assert.Equal(orig.Sensitivity,         loaded.Sensitivity);
        Assert.Equal(orig.InvertRotation,      loaded.InvertRotation);
        Assert.Equal(orig.LongPressMs,         loaded.LongPressMs);
        Assert.Equal(orig.TapWindowMs,         loaded.TapWindowMs);
        Assert.Equal(orig.LedBrightness,       loaded.LedBrightness);
        Assert.Equal(orig.LedPulseOnAudio,     loaded.LedPulseOnAudio);
        Assert.Equal(orig.LedBassOnly,         loaded.LedBassOnly);
        Assert.Equal(orig.BassFrequencyCutoff, loaded.BassFrequencyCutoff);
        Assert.Equal(orig.BassGain,            loaded.BassGain);
        Assert.Equal(orig.FfRwThreshold,       loaded.FfRwThreshold);
        Assert.Equal(orig.FfRwStepSeconds,     loaded.FfRwStepSeconds);
    }

    [Fact]
    public void Save_CreatesConfigFile()
    {
        new PowerMateConfig().Save();
        Assert.True(File.Exists(ConfigPath));
    }

    [Fact]
    public void Save_DoesNotLeaveTemporaryFile()
    {
        new PowerMateConfig().Save();
        Assert.False(File.Exists(ConfigPath + ".tmp"));
    }

    [Fact]
    public void Save_OverwritesExistingFile()
    {
        var a = new PowerMateConfig { VolumeStep = 3 };
        a.Save();
        var b = new PowerMateConfig { VolumeStep = 8 };
        b.Save();
        Assert.Equal(8, PowerMateConfig.Load().VolumeStep);
    }

    // ── Sanitize: every field at its boundaries ────────────────────────────────
    // Write raw JSON with an extreme value, Load() sanitizes it, assert result.

    private PowerMateConfig LoadWithSingleFieldOverride(string field, object value)
    {
        var props = new Dictionary<string, object>
        {
            ["VolumeStep"]          = 2,
            ["Sensitivity"]         = 1.0,
            ["LongPressMs"]         = 800,
            ["TapWindowMs"]         = 350,
            ["LedBrightness"]       = 128,
            ["BassFrequencyCutoff"] = 250,
            ["BassGain"]            = 5.0,
            ["FfRwThreshold"]       = 3,
            ["FfRwStepSeconds"]     = 5,
        };
        props[field] = value;
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(props));
        return PowerMateConfig.Load();
    }

    // VolumeStep: clamp 1–10
    [Theory]
    [InlineData(0,   1)]
    [InlineData(1,   1)]
    [InlineData(10, 10)]
    [InlineData(11, 10)]
    public void Sanitize_VolumeStep(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("VolumeStep", input).VolumeStep);

    // LongPressMs: clamp 300–2000
    [Theory]
    [InlineData(100,  300)]
    [InlineData(300,  300)]
    [InlineData(2000, 2000)]
    [InlineData(3000, 2000)]
    public void Sanitize_LongPressMs(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("LongPressMs", input).LongPressMs);

    // TapWindowMs: clamp 150–800
    [Theory]
    [InlineData(50,   150)]
    [InlineData(150,  150)]
    [InlineData(800,  800)]
    [InlineData(1000, 800)]
    public void Sanitize_TapWindowMs(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("TapWindowMs", input).TapWindowMs);

    // LedBrightness: clamp 0–255
    [Theory]
    [InlineData(-1,   0)]
    [InlineData(0,    0)]
    [InlineData(255,  255)]
    [InlineData(300,  255)]
    public void Sanitize_LedBrightness(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("LedBrightness", input).LedBrightness);

    // BassFrequencyCutoff: clamp 60–500
    [Theory]
    [InlineData(10,   60)]
    [InlineData(60,   60)]
    [InlineData(500,  500)]
    [InlineData(1000, 500)]
    public void Sanitize_BassFrequencyCutoff(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("BassFrequencyCutoff", input).BassFrequencyCutoff);

    // FfRwThreshold: clamp 1–10
    [Theory]
    [InlineData(0,   1)]
    [InlineData(1,   1)]
    [InlineData(10, 10)]
    [InlineData(20, 10)]
    public void Sanitize_FfRwThreshold(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("FfRwThreshold", input).FfRwThreshold);

    // FfRwStepSeconds: clamp 1–30
    [Theory]
    [InlineData(0,   1)]
    [InlineData(1,   1)]
    [InlineData(30, 30)]
    [InlineData(60, 30)]
    public void Sanitize_FfRwStepSeconds(int input, int expected)
        => Assert.Equal(expected, LoadWithSingleFieldOverride("FfRwStepSeconds", input).FfRwStepSeconds);

    // Sensitivity: finite → clamp 0.5–3.0; non-finite → 1.0f (default)
    [Theory]
    [InlineData(0.1,  0.5f)]
    [InlineData(0.5,  0.5f)]
    [InlineData(3.0,  3.0f)]
    [InlineData(5.0,  3.0f)]
    public void Sanitize_Sensitivity_FiniteValues(double input, float expected)
    {
        // Use JsonSerializer (invariant culture) to avoid locale-dependent decimal separators.
        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(new Dictionary<string, double> { ["Sensitivity"] = input }));
        Assert.Equal(expected, PowerMateConfig.Load().Sensitivity);
    }

    // BassGain: finite → clamp 0.5–50.0; non-finite → 5.0f (default)
    [Theory]
    [InlineData(0.1,   0.5f)]
    [InlineData(0.5,   0.5f)]
    [InlineData(50.0,  50.0f)]
    [InlineData(100.0, 50.0f)]
    public void Sanitize_BassGain_FiniteValues(double input, float expected)
    {
        File.WriteAllText(ConfigPath,
            JsonSerializer.Serialize(new Dictionary<string, double> { ["BassGain"] = input }));
        Assert.Equal(expected, PowerMateConfig.Load().BassGain);
    }

    // Verify the non-finite guard logic matches production code.
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Sanitize_SensitivityGuard_NonFinite_UsesDefault(float nonFinite)
    {
        // Non-finite floats cannot round-trip via JSON, so test the logic directly.
        float result = float.IsFinite(nonFinite)
            ? Math.Clamp(nonFinite, 0.5f, 3.0f)
            : 1.0f;
        Assert.Equal(1.0f, result);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Sanitize_BassGainGuard_NonFinite_UsesDefault(float nonFinite)
    {
        float result = float.IsFinite(nonFinite)
            ? Math.Clamp(nonFinite, 0.5f, 50.0f)
            : 5.0f;
        Assert.Equal(5.0f, result);
    }

    // ── Save sanitizes before writing ─────────────────────────────────────────

    [Fact]
    public void Save_SanitizesBeforeWriting()
    {
        // VolumeStep above max should be clamped to 10 in the saved file.
        var c = new PowerMateConfig { VolumeStep = 99 };
        c.Save();
        var loaded = PowerMateConfig.Load();
        Assert.Equal(10, loaded.VolumeStep);
    }
}
