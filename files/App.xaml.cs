using PowerMateSettings.Models;
using PowerMateSettings.Views;

namespace PowerMateSettings;

public partial class App : Application
{
    public static PowerMateConfig Config { get; private set; } = new();

    public App()
    {
        InitializeComponent();
        ParseArgs();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(new SettingsPage()));
    }

    private static void ParseArgs()
    {
        var args = Environment.GetCommandLineArgs();
        // Expected args:
        //   --config <path>
        //   --volume <0-100>
        //   --muted <true|false>
        //   --connected <true|false>
        //   --startup <true|false>

        for (int i = 1; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--config":
                    Config.ConfigPath = args[i + 1];
                    var loaded = PowerMateConfig.Load(args[i + 1]);
                    loaded.ConfigPath = args[i + 1];
                    Config = loaded;
                    break;
                case "--volume":
                    if (int.TryParse(args[i + 1], out int vol))
                        Config.CurrentVolume = vol;
                    break;
                case "--muted":
                    Config.IsMuted = args[i + 1].ToLower() == "true";
                    break;
                case "--connected":
                    Config.DeviceConnected = args[i + 1].ToLower() == "true";
                    break;
                case "--startup":
                    Config.StartWithWindows = args[i + 1].ToLower() == "true";
                    break;
            }
        }
    }
}
