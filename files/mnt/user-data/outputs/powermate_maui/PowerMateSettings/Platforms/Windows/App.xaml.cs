using Microsoft.UI.Xaml;

// This is the entry point for the Windows platform.
// It is required by .NET MAUI on Windows.

namespace PowerMateSettings.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
