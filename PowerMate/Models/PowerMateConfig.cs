using System.Text.Json;

namespace PowerMate.Models;

public class PowerMateConfig
{
    public int VolumeStep { get; set; } = 2;
    public float Sensitivity { get; set; } = 1.0f;
    public bool InvertRotation { get; set; } = false;
    public int LongPressMs { get; set; } = 800;
    public int TapWindowMs { get; set; } = 350;
    public int LedBrightness { get; set; } = 255;
    public bool LedPulseOnAudio { get; set; } = false;
    public bool LedBassOnly { get; set; } = false;
    public int BassFrequencyCutoff { get; set; } = 250;
    public float BassGain { get; set; } = 5.0f;
    public int FfRwThreshold { get; set; } = 3;         // rotation steps needed to enter FF/RW
    public double FfRwStepSeconds { get; set; } = 0.5;  // seconds seeked per detent (sub-second: a natural spin emits many)
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;

    // Settable by tests via InternalsVisibleTo; null = use the real AppData path.
    internal static string? TestConfigPath;

    private static string ConfigPath =>
        TestConfigPath
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PowerMate", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static PowerMateConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var cfg = JsonSerializer.Deserialize<PowerMateConfig>(
                    File.ReadAllText(ConfigPath), JsonOptions);
                if (cfg != null) return Sanitize(cfg);
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var json = JsonSerializer.Serialize(Sanitize(this), JsonOptions);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, ConfigPath, overwrite: true);
        }
        catch { }
    }

    private static PowerMateConfig Sanitize(PowerMateConfig c)
    {
        c.VolumeStep           = Math.Clamp(c.VolumeStep, 1, 10);
        c.Sensitivity          = float.IsFinite(c.Sensitivity) ? Math.Clamp(c.Sensitivity, 0.5f, 3.0f) : 1.0f;
        c.LongPressMs          = Math.Clamp(c.LongPressMs, 300, 2000);
        c.TapWindowMs          = Math.Clamp(c.TapWindowMs, 150, 800);
        c.LedBrightness        = Math.Clamp(c.LedBrightness, 0, 255);
        c.BassFrequencyCutoff  = Math.Clamp(c.BassFrequencyCutoff, 60, 500);
        c.BassGain             = float.IsFinite(c.BassGain) ? Math.Clamp(c.BassGain, 0.5f, 50.0f) : 5.0f;
        c.FfRwThreshold        = Math.Clamp(c.FfRwThreshold, 1, 10);
        c.FfRwStepSeconds      = double.IsFinite(c.FfRwStepSeconds) ? Math.Clamp(c.FfRwStepSeconds, 0.1, 2.0) : 0.5;
        return c;
    }
}
