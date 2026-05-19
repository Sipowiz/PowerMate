using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerMate;
using PowerMate.Services;
using PowerMate.Views;
using System.Runtime.InteropServices;
using System.Windows.Input;
using WinMenuFlyout          = Microsoft.UI.Xaml.Controls.MenuFlyout;
using WinMenuFlyoutItem      = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using WinMenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using MauiControlsApp        = Microsoft.Maui.Controls.Application;
using MauiWindow             = Microsoft.Maui.Controls.Window;

namespace PowerMate.WinUI;

public partial class App : MauiWinUIApplication
{
    private TaskbarIcon? _trayIcon;
    private MauiWindow? _settingsWindow;
    private MauiWindow? _creditsWindow;
    private DispatcherQueue _dq = null!;

    // Cached state for icon rendering
    private float           _currentVolume;
    private bool            _isMuted;
    private InteractionMode _interactionMode  = InteractionMode.Idle;
    private PlaybackState   _playbackState    = PlaybackState.Unknown;
    private PlaybackState   _smtcState        = PlaybackState.Unknown;
    private Timer?          _flashTimer;
    private IMediaSessionService? _mediaService;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    private const int WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG   = 1;

    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dq = DispatcherQueue.GetForCurrentThread();
        base.OnLaunched(args);

        SetupTrayIcon();
        StartController();

        _dq.TryEnqueue(DispatcherQueuePriority.Low, HideMainWindow);
    }

    private void HideMainWindow()
    {
        var mauiApp    = IPlatformApplication.Current?.Application as MauiControlsApp;
        var mauiWindow = mauiApp?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            win.AppWindow?.Hide();
    }

    // ── Tray icon setup ───────────────────────────────────────────────────────

    private void SetupTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText    = "PowerMate Driver — Disconnected",
                MenuActivation = PopupActivationMode.RightClick,
                ContextMenuMode = ContextMenuMode.SecondWindow,
                DoubleClickCommand = new RelayCommand(OpenSettingsWindow),
            };

            _trayIcon.UpdateIcon(TrayIconRenderer.Render(0f, false));

            var flyout = new WinMenuFlyout();

            var settingsItem = new WinMenuFlyoutItem { Text = "Settings" };
            settingsItem.Click += (_, _) => OpenSettingsWindow();
            flyout.Items.Add(settingsItem);

            var aboutItem = new WinMenuFlyoutItem { Text = "About" };
            aboutItem.Click += (_, _) => OpenCreditsWindow();
            flyout.Items.Add(aboutItem);

            flyout.Items.Add(new WinMenuFlyoutSeparator());

            var quitItem = new WinMenuFlyoutItem { Text = "Quit" };
            quitItem.Click += (_, _) => QuitApp();
            flyout.Items.Add(quitItem);

            _trayIcon.ContextFlyout = flyout;
            _trayIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] setup failed: {ex}");
        }
    }

    // ── Controller wiring ─────────────────────────────────────────────────────

    private void StartController()
    {
        var services = MauiProgram.Current?.Services;
        if (services == null) return;

        var controller = services.GetRequiredService<PowerMateController>();
        _mediaService  = services.GetRequiredService<IMediaSessionService>();

        controller.Start();

        var audio = services.GetRequiredService<IAudioService>();
        _currentVolume = audio.GetLevel();
        _isMuted       = audio.IsMuted();
        RefreshTrayIcon();

        controller.VolumeChanged += (vol, muted) =>
        {
            _currentVolume = vol;
            _isMuted       = muted;
            _dq.TryEnqueue(RefreshTrayIcon);
        };

        controller.ConnectionChanged += connected =>
            _dq.TryEnqueue(() =>
            {
                if (_trayIcon != null)
                    _trayIcon.ToolTipText = connected
                        ? "PowerMate — Connected"
                        : "PowerMate — Disconnected";
            });

        controller.InteractionModeChanged += mode =>
        {
            _interactionMode = mode;
            _dq.TryEnqueue(RefreshTrayIcon);
        };

        _mediaService.PlaybackStateChanged += state =>
        {
            _smtcState     = state;
            _playbackState = state;
            _dq.TryEnqueue(RefreshTrayIcon);
        };

        controller.SymbolFlash += symbol =>
        {
            _playbackState = symbol;
            _dq.TryEnqueue(RefreshTrayIcon);

            _flashTimer?.Dispose();
            _flashTimer = new Timer(_ =>
            {
                _playbackState = _smtcState;
                _dq.TryEnqueue(RefreshTrayIcon);
            }, null, 600, Timeout.Infinite);
        };
    }

    private void RefreshTrayIcon()
    {
        if (_trayIcon == null) return;

        bool isFfRw       = _interactionMode == InteractionMode.FfRw;
        bool isInteracting = _interactionMode != InteractionMode.Idle;

        float displayValue = isFfRw
            ? (_mediaService?.GetPlaybackPosition() ?? 0f)
            : _currentVolume;

        var icon = TrayIconRenderer.Render(displayValue, _isMuted, _playbackState, isInteracting);
        _trayIcon.UpdateIcon(icon);

        _trayIcon.ToolTipText = _isMuted
            ? "PowerMate — Muted"
            : $"PowerMate — {(int)(_currentVolume * 100)}%";

        UpdateWindowIcon(displayValue, _isMuted, _playbackState, isInteracting);
    }

    // ── Settings / Credits windows ────────────────────────────────────────────

    private void OpenSettingsWindow()
    {
        _dq.TryEnqueue(() =>
        {
            if (_settingsWindow != null)
            {
                if (_settingsWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
                    win.Activate();
                return;
            }

            var services = MauiProgram.Current?.Services;
            if (services == null) return;

            var config = services.GetRequiredService<PowerMate.Models.PowerMateConfig>();
            var page   = services.GetRequiredService<SettingsPage>();
            _settingsWindow = new MauiWindow(page)
            {
                Title         = "PowerMate Settings",
                Width         = 480,
                Height        = 640,
                MinimumWidth  = 400,
                MinimumHeight = 520,
            };

            if (config.WindowX >= 0 && config.WindowY >= 0)
            {
                _settingsWindow.X = config.WindowX;
                _settingsWindow.Y = config.WindowY;
            }

            _settingsWindow.Destroying += (_, _) =>
            {
                if (_settingsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
                {
                    var pos = w.AppWindow.Position;
                    config.WindowX = pos.X;
                    config.WindowY = pos.Y;
                    config.Save();
                }
                _settingsWindow = null;
            };

            _settingsWindow.HandlerChanged += (_, _) =>
            {
                if (_settingsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
                    HookWindowIconOnActivated(w);
            };

            (IPlatformApplication.Current?.Application as MauiControlsApp)
                ?.OpenWindow(_settingsWindow);
        });
    }

    private void OpenCreditsWindow()
    {
        _dq.TryEnqueue(() =>
        {
            if (_creditsWindow != null)
            {
                if (_creditsWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
                    win.Activate();
                return;
            }

            var page = new CreditsPage();
            _creditsWindow = new MauiWindow(page)
            {
                Title         = "About PowerMate",
                Width         = 380,
                Height        = 400,
                MinimumWidth  = 320,
                MinimumHeight = 360,
            };
            _creditsWindow.Destroying += (_, _) => _creditsWindow = null;
            _creditsWindow.HandlerChanged += (_, _) =>
            {
                if (_creditsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window w)
                    HookWindowIconOnActivated(w);
            };

            (IPlatformApplication.Current?.Application as MauiControlsApp)
                ?.OpenWindow(_creditsWindow);
        });
    }

    // ── Taskbar / window icon ─────────────────────────────────────────────────

    /// <summary>
    /// Subscribes to the window's Activated event to set the icon after WinUI
    /// has finished applying its own default — more reliable than priority-queue tricks.
    /// </summary>
    private void HookWindowIconOnActivated(Microsoft.UI.Xaml.Window w)
    {
        Windows.Foundation.TypedEventHandler<object, WindowActivatedEventArgs>? handler = null;
        handler = (_, _) =>
        {
            w.Activated -= handler;
            _dq.TryEnqueue(() => SetWindowIcon(w));
        };
        w.Activated += handler;
    }

    private void SetWindowIcon(Microsoft.UI.Xaml.Window win)
    {
        try
        {
            bool isFfRw        = _interactionMode == InteractionMode.FfRw;
            bool isInteracting = _interactionMode != InteractionMode.Idle;
            float displayValue = isFfRw
                ? (_mediaService?.GetPlaybackPosition() ?? 0f)
                : _currentVolume;

            var icon  = TrayIconRenderer.Render(displayValue, _isMuted, _playbackState, isInteracting);
            var hIcon = icon.Handle;
            var hWnd  = WinRT.Interop.WindowNative.GetWindowHandle(win);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
            SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG,   hIcon);
        }
        catch { }
    }

    private void UpdateWindowIcon(float displayValue, bool muted,
        PlaybackState playbackState, bool interacting)
    {
        try
        {
            if (_settingsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                var icon  = TrayIconRenderer.Render(displayValue, muted, playbackState, interacting);
                var hIcon = icon.Handle;
                var hWnd  = WinRT.Interop.WindowNative.GetWindowHandle(win);
                SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, hIcon);
                SendMessage(hWnd, WM_SETICON, (IntPtr)ICON_BIG,   hIcon);
            }
        }
        catch { }
    }

    // ── Quit ──────────────────────────────────────────────────────────────────

    private void QuitApp()
    {
        _flashTimer?.Dispose();
        _trayIcon?.Dispose();
        MauiProgram.Current?.Services.GetService<PowerMateController>()?.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private sealed class RelayCommand(Action execute) : ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
