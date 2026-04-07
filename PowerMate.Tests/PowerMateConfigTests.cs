using PowerMate.Models;

namespace PowerMate.Tests;

public class PowerMateConfigTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var config = new PowerMateConfig();

        Assert.Equal(2, config.VolumeStep);
        Assert.Equal(1.0f, config.Sensitivity);
        Assert.False(config.InvertRotation);
        Assert.Equal(ClickAction.PlayPause, config.ClickAction);
        Assert.Equal(DoubleClickAction.NextTrack, config.DoubleClickAction);
        Assert.Equal(TripleClickAction.PreviousTrack, config.TripleClickAction);
        Assert.Equal(LongPressAction.Mute, config.LongPressAction);
        Assert.Equal(800, config.LongPressMs);
        Assert.Equal(128, config.LedBrightness);
        Assert.False(config.LedPulseOnAudio);
        Assert.False(config.LedBassOnly);
        Assert.Equal(250, config.BassFrequencyCutoff);
        Assert.Equal(5.0f, config.BassGain);
        Assert.False(config.StartWithWindows);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"powermate_test_{Guid.NewGuid():N}");
        var configPath = Path.Combine(tempDir, "config.json");
        Directory.CreateDirectory(tempDir);

        try
        {
            var config = new PowerMateConfig
            {
                VolumeStep = 5,
                Sensitivity = 2.0f,
                InvertRotation = true,
                ClickAction = ClickAction.Mute,
                DoubleClickAction = DoubleClickAction.PlayPause,
                TripleClickAction = TripleClickAction.Mute,
                LongPressAction = LongPressAction.PlayPause,
                LongPressMs = 500,
                LedBrightness = 200,
                LedPulseOnAudio = true,
                LedBassOnly = true,
                BassFrequencyCutoff = 100,
                BassGain = 10.0f,
                StartWithWindows = true,
            };

            // Serialize
            var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            });
            File.WriteAllText(configPath, json);

            // Deserialize
            var loaded = System.Text.Json.JsonSerializer.Deserialize<PowerMateConfig>(
                File.ReadAllText(configPath), new System.Text.Json.JsonSerializerOptions
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                })!;

            Assert.Equal(5, loaded.VolumeStep);
            Assert.Equal(2.0f, loaded.Sensitivity);
            Assert.True(loaded.InvertRotation);
            Assert.Equal(ClickAction.Mute, loaded.ClickAction);
            Assert.Equal(DoubleClickAction.PlayPause, loaded.DoubleClickAction);
            Assert.Equal(TripleClickAction.Mute, loaded.TripleClickAction);
            Assert.Equal(LongPressAction.PlayPause, loaded.LongPressAction);
            Assert.Equal(500, loaded.LongPressMs);
            Assert.Equal(200, loaded.LedBrightness);
            Assert.True(loaded.LedPulseOnAudio);
            Assert.True(loaded.LedBassOnly);
            Assert.Equal(100, loaded.BassFrequencyCutoff);
            Assert.Equal(10.0f, loaded.BassGain);
            Assert.True(loaded.StartWithWindows);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Theory]
    [InlineData("PlayPause", ClickAction.PlayPause)]
    [InlineData("Mute", ClickAction.Mute)]
    [InlineData("None", ClickAction.None)]
    public void EnumSerialization_UsesStringNames(string expected, ClickAction action)
    {
        var config = new PowerMateConfig { ClickAction = action };
        var json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        Assert.Contains($"\"{expected}\"", json);
    }
}
