using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerMate.Models;

public class PowerMateConfig
{
    public int VolumeStep { get; set; } = 2;
    public float Sensitivity { get; set; } = 1.0f;
    public bool InvertRotation { get; set; } = false;
    public ClickAction ClickAction { get; set; } = ClickAction.PlayPause;
    public LongPressAction LongPressAction { get; set; } = LongPressAction.Mute;
    public int LongPressMs { get; set; } = 800;
    public DoubleClickAction DoubleClickAction { get; set; } = DoubleClickAction.NextTrack;
    public TripleClickAction TripleClickAction { get; set; } = TripleClickAction.PreviousTrack;
    public int LedBrightness { get; set; } = 128;
    public bool LedPulseOnAudio  { get; set; } = false;
    public bool LedBassOnly { get; set; } = false;
    public int BassFrequencyCutoff { get; set; } = 250;
    public float BassGain { get; set; } = 5.0f;
    public bool StartWithWindows { get; set; } = false;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;

    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PowerMate", "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PowerMateConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize<PowerMateConfig>(
                    File.ReadAllText(ConfigPath), JsonOptions) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }
}

public enum ClickAction { PlayPause, Mute, None }
public enum DoubleClickAction { NextTrack, PlayPause, Mute, None }
public enum TripleClickAction { PreviousTrack, PlayPause, Mute, None }
public enum LongPressAction { Mute, PlayPause, None }
