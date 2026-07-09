using System.Runtime.InteropServices;
using H.NotifyIcon;
using Serilog;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using PowerMate;
using PowerMate.Services;
using PowerMate.Views;
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

    private PowerMateController? _controller;

    // Cached state for icon rendering
    private float           _currentVolume;
    private bool            _isMuted;
    private InteractionMode _interactionMode  = InteractionMode.Idle;
    private PlaybackState   _playbackState    = PlaybackState.Unknown;
    private PlaybackState   _smtcState        = PlaybackState.Unknown;
    private Timer?          _flashTimer;
    private IMediaSessionService? _mediaService;

    // AppWindow.SetIcon caches by path, so we rotate filenames to force a reload.
    private string _tempIconPath = "";
    private int    _tempIconGen;

    // ── WndProc subclass for WM_POWERBROADCAST ────────────────────────────────
    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);
    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(
        IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);
    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(
        IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData);

    private const uint WM_POWERBROADCAST    = 0x0218;
    private const int  PBT_APMSUSPEND       = 0x0004;
    private const int  PBT_APMRESUMESUSPEND = 0x0007;

    private SubclassProc? _subclassProc; // pin delegate to prevent GC
    private IntPtr        _subclassedHwnd;

    private IntPtr OnWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_POWERBROADCAST)
        {
            int p = (int)wParam;
            try
            {
                if      (p == PBT_APMSUSPEND)       _controller?.Suspend();
                else if (p == PBT_APMRESUMESUSPEND) _controller?.Resume();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "[{Source}] wParam={P}", "WndProcPowerBroadcast", p);
                Log.CloseAndFlush();
                MauiProgram.ConfigureLogging();
            }
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private void InstallPowerWatcher(IntPtr hwnd)
    {
        _subclassProc   = OnWindowMessage;
        _subclassedHwnd = hwnd;
        SetWindowSubclass(hwnd, _subclassProc, (IntPtr)1, IntPtr.Zero);
    }

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, "[{Source}]", "WinUIUnhandledException");
            Log.CloseAndFlush();
            e.Handled = true;
        };
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
        {
            win.AppWindow?.Hide();
            var hwnd = WindowNative.GetWindowHandle(win);
            if (hwnd != IntPtr.Zero)
                InstallPowerWatcher(hwnd);
        }
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

        _controller   = services.GetRequiredService<PowerMateController>();
        _mediaService = services.GetRequiredService<IMediaSessionService>();

        _controller.Start();

        var audio = services.GetRequiredService<IAudioService>();
        _currentVolume = audio.GetLevel();
        _isMuted       = audio.IsMuted();
        RefreshTrayIcon();

        _controller.VolumeChanged += (vol, muted) =>
        {
            _currentVolume = vol;
            _isMuted       = muted;
            _dq.TryEnqueue(RefreshTrayIcon);
        };

        _controller.ConnectionChanged += connected =>
            _dq.TryEnqueue(() =>
            {
                if (_trayIcon != null)
                    _trayIcon.ToolTipText = connected
                        ? "PowerMate — Connected"
                        : "PowerMate — Disconnected";
            });

        _controller.InteractionModeChanged += mode =>
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

        _controller.SymbolFlash += symbol =>
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
            ? (_controller?.GetFfRwFraction() ?? 0f)   // seek target, not stale SMTC position
            : _currentVolume;

        var icon = TrayIconRenderer.Render(displayValue, _isMuted, _playbackState, isInteracting);
        _trayIcon.UpdateIcon(icon);

        _trayIcon.ToolTipText = _isMuted
            ? "PowerMate — Muted"
            : $"PowerMate — {(int)(_currentVolume * 100)}%";

        UpdateWindowIcon(icon);
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
        if (_tempIconPath.Length == 0) return;
        try { win.AppWindow.SetIcon(_tempIconPath); }
        catch { }
    }

    private void UpdateWindowIcon(System.Drawing.Icon icon)
    {
        // Use a new filename each call — AppWindow.SetIcon caches by path in Release builds.
        var newPath = Path.Combine(Path.GetTempPath(), $"PowerMate_icon_{++_tempIconGen}.ico");
        var oldPath = _tempIconPath;

        try
        {
            using var fs = new FileStream(newPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            icon.Save(fs);
        }
        catch { return; }

        _tempIconPath = newPath;

        try
        {
            if (_settingsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window sw)
                sw.AppWindow.SetIcon(newPath);
            if (_creditsWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window cw)
                cw.AppWindow.SetIcon(newPath);
        }
        catch { }

        // Delete previous file now that AppWindow has loaded the new one.
        if (oldPath.Length > 0) try { File.Delete(oldPath); } catch { }
    }

    // ── Quit ──────────────────────────────────────────────────────────────────

    private void QuitApp()
    {
        if (_subclassedHwnd != IntPtr.Zero && _subclassProc != null)
        {
            RemoveWindowSubclass(_subclassedHwnd, _subclassProc, (IntPtr)1);
            _subclassedHwnd = IntPtr.Zero;
            _subclassProc   = null;
        }
        _flashTimer?.Dispose();
        _trayIcon?.Dispose();
        _controller?.Dispose();
        if (_tempIconPath.Length > 0) try { File.Delete(_tempIconPath); } catch { }
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
