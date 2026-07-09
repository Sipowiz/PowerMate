using System.ComponentModel;
using System.Runtime.CompilerServices;
using PowerMate.Models;
using PowerMate.Services;
using Serilog;

namespace PowerMate.ViewModels;

public class SettingsViewModel : INotifyPropertyChanged
{
    private readonly PowerMateController _controller;
    private readonly IAudioService _audio;
    private readonly UpdateService _updateService;
    private PowerMateConfig _config;
    private Timer? _saveTimer;
    private const int SaveDebounceMs = 400;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SettingsViewModel(PowerMateController controller, IAudioService audio,
        PowerMateConfig config, UpdateService updateService)
    {
        _controller    = controller;
        _audio         = audio;
        _config        = config;
        _updateService = updateService;

        controller.VolumeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CurrentVolumeText));
            OnPropertyChanged(nameof(CurrentVolumePercent));
        };
        controller.ConnectionChanged += _ =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ConnectionStatusColor));
        };
    }

    // ── Status ────────────────────────────────────────────────────────────────
    public bool IsConnected => _controller.IsConnected;
    public string ConnectionStatusText  => _controller.IsConnected ? "Connected" : "Disconnected";
    public Color  ConnectionStatusColor => _controller.IsConnected
        ? Color.FromArgb("#16C60C")
        : Color.FromArgb("#FF4444");
    public string CurrentVolumeText    => $"{(int)(_audio.GetLevel() * 100)}%";
    public int    CurrentVolumePercent => (int)(_audio.GetLevel() * 100);

    // ── Volume ────────────────────────────────────────────────────────────────
    public int VolumeStep
    {
        get => _config.VolumeStep;
        set { _config.VolumeStep = Math.Clamp(value, 1, 10); Notify(); }
    }

    public float Sensitivity
    {
        get => _config.Sensitivity;
        set
        {
            _config.Sensitivity = (float)Math.Round(Math.Clamp(value, 0.5, 3.0), 1);
            Notify();
            Notify(nameof(SensitivityText));
        }
    }
    public string SensitivityText => $"{Sensitivity:0.0}×";

    public bool InvertRotation
    {
        get => _config.InvertRotation;
        set { _config.InvertRotation = value; Notify(); }
    }

    // ── Button timing ─────────────────────────────────────────────────────────
    public int TapWindowMs
    {
        get => _config.TapWindowMs;
        set { _config.TapWindowMs = Math.Clamp(value, 150, 800); Notify(); Notify(nameof(TapWindowMsText)); }
    }
    public string TapWindowMsText => $"{TapWindowMs} ms";

    public int LongPressMs
    {
        get => _config.LongPressMs;
        set { _config.LongPressMs = Math.Clamp(value, 300, 2000); Notify(); Notify(nameof(LongPressMsText)); }
    }
    public string LongPressMsText => $"{LongPressMs} ms";

    // ── FF/RW ─────────────────────────────────────────────────────────────────
    public int FfRwStepSeconds
    {
        get => _config.FfRwStepSeconds;
        set { _config.FfRwStepSeconds = Math.Clamp(value, 1, 30); Notify(); Notify(nameof(FfRwStepText)); }
    }
    public string FfRwStepText => $"{FfRwStepSeconds} s";

    // ── LED ───────────────────────────────────────────────────────────────────
    public int LedBrightness
    {
        get => _config.LedBrightness;
        set { _config.LedBrightness = Math.Clamp(value, 0, 255); Notify(); Notify(nameof(LedBrightnessText)); }
    }
    public string LedBrightnessText => $"{(int)(_config.LedBrightness / 2.55):0}%";

    public bool LedPulseOnAudio
    {
        get => _config.LedPulseOnAudio;
        set { _config.LedPulseOnAudio = value; Notify(); Notify(nameof(ShowBassOptions)); }
    }
    public bool ShowBassOptions => _config.LedPulseOnAudio;

    public bool LedBassOnly
    {
        get => _config.LedBassOnly;
        set { _config.LedBassOnly = value; Notify(); Notify(nameof(ShowBassFrequency)); }
    }
    public bool ShowBassFrequency => _config.LedBassOnly && _config.LedPulseOnAudio;

    public int BassFrequencyCutoff
    {
        get => _config.BassFrequencyCutoff;
        set { _config.BassFrequencyCutoff = Math.Clamp(value, 60, 500); Notify(); Notify(nameof(BassFrequencyCutoffText)); }
    }
    public string BassFrequencyCutoffText => $"{BassFrequencyCutoff} Hz";

    public float BassGain
    {
        get => _config.BassGain;
        set
        {
            _config.BassGain = (float)Math.Round(Math.Clamp(value, 0.5, 50.0), 1);
            Notify();
            Notify(nameof(BassGainText));
        }
    }
    public string BassGainText => $"{BassGain:0.0}×";

    // Live bass level meter (0-100) for the settings page bar
    private int _bassLevelPercent;
    public int BassLevelPercent
    {
        get => _bassLevelPercent;
        private set
        {
            if (_bassLevelPercent == value) return;
            _bassLevelPercent = value;
            OnPropertyChanged(nameof(BassLevelPercent));
            OnPropertyChanged(nameof(BassLevelText));
            OnPropertyChanged(nameof(BassLevelWidth));
            OnPropertyChanged(nameof(BassLevelRemaining));
        }
    }
    public string     BassLevelText      => $"{_bassLevelPercent}%";
    public GridLength BassLevelWidth     => new(_bassLevelPercent, GridUnitType.Star);
    public GridLength BassLevelRemaining => new(100 - _bassLevelPercent, GridUnitType.Star);

    private Timer? _levelPollTimer;

    public void StartLevelPolling()
    {
        _levelPollTimer?.Dispose();
        _levelPollTimer = new Timer(_ =>
        {
            int level;
            if (_config.LedBassOnly && _config.LedPulseOnAudio)
                level = (int)(_audio.GetBassPeak() * 100);
            else if (_config.LedPulseOnAudio)
                level = (int)(_audio.GetPeakLevel() * 100);
            else
                level = 0;

            MainThread.BeginInvokeOnMainThread(() => BassLevelPercent = level);
        }, null, 0, 80);
    }

    public void StopLevelPolling()
    {
        _levelPollTimer?.Dispose();
        _levelPollTimer = null;
    }

    // ── System ────────────────────────────────────────────────────────────────
    // Reads the Run key directly rather than a cached copy, so the switch always
    // reflects what Windows will actually do — including what the installer set.
    public bool StartWithWindows
    {
        get => StartupService.IsEnabled();
        set
        {
            try { StartupService.Set(value); }
            catch (Exception ex) { Log.Error(ex, "[{Source}]", "StartupService.Set"); }
            OnPropertyChanged(nameof(StartWithWindows)); // snaps back if the write failed
        }
    }

    // ── Updates ───────────────────────────────────────────────────────────────
    private bool   _updateAvailable;
    private string _updateVersion = "";
    private string _updateUrl     = "";

    public bool UpdateAvailable
    {
        get => _updateAvailable;
        private set { if (_updateAvailable != value) { _updateAvailable = value; OnPropertyChanged(nameof(UpdateAvailable)); } }
    }
    public string UpdateVersion
    {
        get => _updateVersion;
        private set { _updateVersion = value; OnPropertyChanged(nameof(UpdateVersion)); OnPropertyChanged(nameof(UpdateText)); }
    }
    public string UpdateUrl
    {
        get => _updateUrl;
        private set { _updateUrl = value; OnPropertyChanged(nameof(UpdateUrl)); }
    }
    public string UpdateText      => _updateAvailable ? $"Version {_updateVersion} available" : "You're up to date";
    public Color  UpdateTextColor => _updateAvailable ? Color.FromArgb("#0A84FF") : Color.FromArgb("#8E8E93");

    public async Task CheckForUpdatesAsync()
    {
        var (available, version, url) = await _updateService.CheckForUpdateAsync();
        UpdateAvailable = available;
        UpdateVersion   = version;
        UpdateUrl       = url;
    }

    // ── Auto-save ─────────────────────────────────────────────────────────────
    private void AutoSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ =>
        {
            _config.Save();
            _controller.UpdateConfig(_config);
        }, null, SaveDebounceMs, Timeout.Infinite);
    }

    private void Notify([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        AutoSave();
    }

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
