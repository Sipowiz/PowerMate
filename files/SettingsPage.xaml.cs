using Microsoft.Win32;
using PowerMateSettings.Models;

namespace PowerMateSettings.Views;

public partial class SettingsPage : ContentPage
{
    private readonly PowerMateConfig _cfg;
    private System.Timers.Timer? _volumeTimer;

    // Maps for picker <-> raw value
    private static readonly List<(string Raw, string Display)> ClickActions = new()
    {
        ("mute",        "Mute / Unmute"),
        ("play_pause",  "Play / Pause"),
        ("next_track",  "Next Track"),
        ("prev_track",  "Previous Track"),
        ("none",        "None"),
    };

    private static readonly List<(string Raw, string Display)> LongActions = new()
    {
        ("none",        "None"),
        ("mute",        "Mute / Unmute"),
        ("media_stop",  "Stop Media"),
        ("play_pause",  "Play / Pause"),
    };

    public SettingsPage()
    {
        InitializeComponent();
        _cfg = App.Config;
        LoadValues();
        StartVolumePoller();
    }

    private void LoadValues()
    {
        // Sliders
        SliderVolumeStep.Value    = _cfg.VolumeStep;
        SliderSensitivity.Value   = _cfg.Sensitivity;
        SliderLongMs.Value        = _cfg.LongPressMs;
        SliderLedBrightness.Value = _cfg.LedBrightness;

        // Labels
        LblVolumeStep.Text    = $"{_cfg.VolumeStep}%";
        LblSensitivity.Text   = $"{_cfg.Sensitivity}x";
        LblLongMs.Text        = $"{_cfg.LongPressMs}ms";
        LblLedBrightness.Text = $"{_cfg.LedBrightness}";

        // Switches
        SwitchInvert.IsToggled        = _cfg.InvertRotation;
        SwitchPulse.IsToggled         = _cfg.LedPulseOnVolume;
        SwitchStartup.IsToggled       = _cfg.StartWithWindows;
        SwitchNotifications.IsToggled = _cfg.Notifications;

        // Pickers
        PickerClick.ItemsSource = ClickActions.Select(x => x.Display).ToList();
        PickerLong.ItemsSource  = LongActions.Select(x => x.Display).ToList();

        var clickIdx = ClickActions.FindIndex(x => x.Raw == _cfg.ClickAction);
        var longIdx  = LongActions.FindIndex(x => x.Raw == _cfg.LongPressAction);
        PickerClick.SelectedIndex = clickIdx >= 0 ? clickIdx : 0;
        PickerLong.SelectedIndex  = longIdx  >= 0 ? longIdx  : 0;

        // Connection pill
        if (_cfg.DeviceConnected)
        {
            ConnPill.BackgroundColor  = Color.FromArgb("#0d2e0d");
            ConnLabel.Text            = "● Connected";
            ConnLabel.TextColor       = Color.FromArgb("#44cc66");
        }
        else
        {
            ConnPill.BackgroundColor  = Color.FromArgb("#2e0d0d");
            ConnLabel.Text            = "○ Disconnected";
            ConnLabel.TextColor       = Color.FromArgb("#dd4444");
        }

        UpdateVolumeLabel();
    }

    private void UpdateVolumeLabel()
    {
        var txt = _cfg.IsMuted
            ? $"🔇  Muted  ({_cfg.CurrentVolume}%)"
            : $"🔊  Volume: {_cfg.CurrentVolume}%";
        LblVolumeStatus.Text = txt;
    }

    // Poll the shared status file written by the Python tray every 500ms
    private void StartVolumePoller()
    {
        _volumeTimer = new System.Timers.Timer(500);
        _volumeTimer.Elapsed += (_, _) =>
        {
            try
            {
                var statusPath = Path.Combine(
                    Path.GetDirectoryName(_cfg.ConfigPath) ?? "", "powermate_status.json");
                if (!File.Exists(statusPath)) return;

                var json   = File.ReadAllText(statusPath);
                var status = System.Text.Json.JsonSerializer.Deserialize<StatusFile>(json);
                if (status is null) return;

                MainThread.BeginInvokeOnMainThread(() =>
                {
                    _cfg.CurrentVolume = status.Volume;
                    _cfg.IsMuted       = status.Muted;
                    UpdateVolumeLabel();
                });
            }
            catch { /* file may be locked */ }
        };
        _volumeTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _volumeTimer?.Stop();
        _volumeTimer?.Dispose();
    }

    // ── Slider handlers ────────────────────────────────────────────────────────
    private void OnVolumeStepChanged(object s, ValueChangedEventArgs e)
    {
        var v = (int)Math.Round(e.NewValue);
        LblVolumeStep.Text = $"{v}%";
    }

    private void OnSensitivityChanged(object s, ValueChangedEventArgs e)
    {
        var v = (int)Math.Round(e.NewValue);
        LblSensitivity.Text = $"{v}x";
    }

    private void OnLongMsChanged(object s, ValueChangedEventArgs e)
    {
        var v = (int)Math.Round(e.NewValue);
        LblLongMs.Text = $"{v}ms";
    }

    private void OnLedBrightnessChanged(object s, ValueChangedEventArgs e)
    {
        var v = (int)Math.Round(e.NewValue);
        LblLedBrightness.Text = $"{v}";
    }

    // ── Save ───────────────────────────────────────────────────────────────────
    private async void OnSave(object sender, EventArgs e)
    {
        _cfg.VolumeStep       = (int)Math.Round(SliderVolumeStep.Value);
        _cfg.Sensitivity      = (int)Math.Round(SliderSensitivity.Value);
        _cfg.InvertRotation   = SwitchInvert.IsToggled;
        _cfg.ClickAction      = ClickActions[Math.Max(0, PickerClick.SelectedIndex)].Raw;
        _cfg.LongPressAction  = LongActions[Math.Max(0, PickerLong.SelectedIndex)].Raw;
        _cfg.LongPressMs      = (int)Math.Round(SliderLongMs.Value);
        _cfg.LedBrightness    = (int)Math.Round(SliderLedBrightness.Value);
        _cfg.LedPulseOnVolume = SwitchPulse.IsToggled;
        _cfg.Notifications    = SwitchNotifications.IsToggled;

        _cfg.Save(_cfg.ConfigPath);
        SetStartup(SwitchStartup.IsToggled);

        // Show saved indicator briefly
        LblSaved.IsVisible = true;
        await Task.Delay(2000);
        LblSaved.IsVisible = false;
    }

    private void OnCancel(object sender, EventArgs e)
    {
        Application.Current?.CloseWindow(Application.Current.Windows[0]);
    }

    // ── Startup registry ───────────────────────────────────────────────────────
    private static void SetStartup(bool enabled)
    {
        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string name    = "GriffinPowerMate";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe    = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
                key.SetValue(name, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        catch { /* registry access denied */ }
    }

    private record StatusFile(
        [property: System.Text.Json.Serialization.JsonPropertyName("volume")] int Volume,
        [property: System.Text.Json.Serialization.JsonPropertyName("muted")]  bool Muted
    );
}
