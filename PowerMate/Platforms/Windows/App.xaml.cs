using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PowerMate;
using PowerMate.Services;
using PowerMate.Views;
using System.Windows.Input;
using WinMenuFlyout = Microsoft.UI.Xaml.Controls.MenuFlyout;
using WinMenuFlyoutItem = Microsoft.UI.Xaml.Controls.MenuFlyoutItem;
using WinMenuFlyoutSeparator = Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator;
using MauiControlsApp = Microsoft.Maui.Controls.Application;
using MauiWindow = Microsoft.Maui.Controls.Window;
using GdiBitmap = System.Drawing.Bitmap;
using GdiImaging = System.Drawing.Imaging;

namespace PowerMate.WinUI;

public partial class App : MauiWinUIApplication
{
    private TaskbarIcon? _trayIcon;
    private MauiWindow? _settingsWindow;
    private DispatcherQueue _dq = null!;

    public App()
    {
        this.InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dq = DispatcherQueue.GetForCurrentThread();
        base.OnLaunched(args);

        // Create the tray icon immediately — it doesn't need the MAUI window
        SetupTrayIcon();
        StartController();

        // Hide the blank window after MAUI finishes wiring up its handler
        _dq.TryEnqueue(DispatcherQueuePriority.Low, HideMainWindow);
    }

    private void HideMainWindow()
    {
        var mauiApp = IPlatformApplication.Current?.Application as MauiControlsApp;
        var mauiWindow = mauiApp?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            win.AppWindow?.Hide();
    }

    private void SetupTrayIcon()
    {
        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "PowerMate Driver — Disconnected",
                MenuActivation = PopupActivationMode.RightClick,
                ContextMenuMode = ContextMenuMode.SecondWindow,
                DoubleClickCommand = new RelayCommand(OpenSettingsWindow),
            };

            // Initial icon — show 0% volume
            _trayIcon.UpdateIcon(TrayIconRenderer.Render(0f, false));

            var flyout = new WinMenuFlyout();

            var settingsItem = new WinMenuFlyoutItem { Text = "Settings" };
            settingsItem.Click += (_, _) => OpenSettingsWindow();
            flyout.Items.Add(settingsItem);

            flyout.Items.Add(new WinMenuFlyoutSeparator());

            var quitItem = new WinMenuFlyoutItem { Text = "Quit" };
            quitItem.Click += (_, _) => QuitApp();
            flyout.Items.Add(quitItem);

            _trayIcon.ContextFlyout = flyout;
            _trayIcon.ForceCreate();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TrayIcon] SetupTrayIcon failed: {ex}");
            System.Diagnostics.Debugger.Break(); // will pause if debugger attached
        }
    }

    private void RefreshTrayIcon(float volume, bool muted)
    {
        _dq.TryEnqueue(() =>
        {
            if (_trayIcon == null) return;
            _trayIcon.UpdateIcon(TrayIconRenderer.Render(volume, muted));
            _trayIcon.ToolTipText = muted
                ? "PowerMate — Muted"
                : $"PowerMate — {(int)(volume * 100)}%";

            UpdateWindowIcon(volume, muted);
        });
    }

    private void StartController()
    {
        var services = MauiProgram.Current?.Services;
        if (services == null) return;

        var controller = services.GetRequiredService<PowerMateController>();
        controller.Start();

        // Render initial icon with real volume
        var audio = services.GetRequiredService<PowerMate.Services.IAudioService>();
        RefreshTrayIcon(audio.GetLevel(), audio.IsMuted());

        controller.VolumeChanged += (vol, muted) =>
            RefreshTrayIcon(vol, muted);

        controller.ConnectionChanged += connected =>
            _dq.TryEnqueue(() =>
            {
                if (_trayIcon != null)
                    _trayIcon.ToolTipText = connected
                        ? "PowerMate Driver — Connected"
                        : "PowerMate Driver — Disconnected";
            });
    }

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

            var page = services.GetRequiredService<SettingsPage>();
            _settingsWindow = new MauiWindow(page)
            {
                Title = "PowerMate Settings",
                Width = 480,
                Height = 660,
                MinimumWidth = 400,
                MinimumHeight = 560,
            };
            _settingsWindow.Destroying += (_, _) => _settingsWindow = null;
            _settingsWindow.HandlerChanged += (s, _) =>
            {
                var audio = MauiProgram.Current?.Services?.GetService<IAudioService>();
                UpdateWindowIcon(audio?.GetLevel() ?? 0f, audio?.IsMuted() ?? false);
            };

            (IPlatformApplication.Current?.Application as MauiControlsApp)
                ?.OpenWindow(_settingsWindow);
        });
    }

    private static readonly string _windowIcoPath =
        Path.Combine(Path.GetTempPath(), "powermate_window.ico");

    private void UpdateWindowIcon(float volume, bool muted)
    {
        try
        {
            if (_settingsWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win) return;

            using var gdiIcon = TrayIconRenderer.Render(volume, muted);
            using var bmp = gdiIcon.ToBitmap();
            using (var fs = new FileStream(_windowIcoPath, FileMode.Create))
            {
                using var icon = System.Drawing.Icon.FromHandle(bmp.GetHicon());
                icon.Save(fs);
            }

            win.AppWindow.SetIcon(_windowIcoPath);
        }
        catch { }
    }

    private void QuitApp()
    {
        _trayIcon?.Dispose();
        MauiProgram.Current?.Services.GetService<PowerMateController>()?.Dispose();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    // Minimal ICommand wrapper
    private sealed class RelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
