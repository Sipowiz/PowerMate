namespace PowerMate;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    // Returns a headless 1×1 window — the real UI lives in the system tray.
    // Platform code in Platforms/Windows/App.xaml.cs hides this immediately.
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new ContentPage { BackgroundColor = Colors.Transparent })
        {
            Width = 1,
            Height = 1,
        };
}