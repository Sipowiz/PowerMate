using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerMateSettings.Models;

public class PowerMateConfig
{
    [JsonPropertyName("volume_step")]
    public int VolumeStep { get; set; } = 2;

    [JsonPropertyName("sensitivity")]
    public int Sensitivity { get; set; } = 1;

    [JsonPropertyName("invert_rotation")]
    public bool InvertRotation { get; set; } = false;

    [JsonPropertyName("click_action")]
    public string ClickAction { get; set; } = "mute";

    [JsonPropertyName("long_press_action")]
    public string LongPressAction { get; set; } = "none";

    [JsonPropertyName("long_press_ms")]
    public int LongPressMs { get; set; } = 800;

    [JsonPropertyName("led_brightness")]
    public int LedBrightness { get; set; } = 255;

    [JsonPropertyName("led_pulse_on_volume")]
    public bool LedPulseOnVolume { get; set; } = false;

    [JsonPropertyName("notifications")]
    public bool Notifications { get; set; } = true;

    // Not stored in config file — passed via command line args
    [JsonIgnore]
    public bool DeviceConnected { get; set; } = false;

    [JsonIgnore]
    public int CurrentVolume { get; set; } = 50;

    [JsonIgnore]
    public bool IsMuted { get; set; } = false;

    [JsonIgnore]
    public bool StartWithWindows { get; set; } = false;

    [JsonIgnore]
    public string ConfigPath { get; set; } = "";

    public static PowerMateConfig Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PowerMateConfig>(json) ?? new PowerMateConfig();
        }
        catch
        {
            return new PowerMateConfig();
        }
    }

    public void Save(string path)
    {
        var opts = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(path, JsonSerializer.Serialize(this, opts));
    }
}
