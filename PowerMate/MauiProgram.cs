using Microsoft.Extensions.Logging;
using PowerMate.Models;
using PowerMate.Services;
using PowerMate.ViewModels;
using PowerMate.Views;

namespace PowerMate;

public static class MauiProgram
{
    public static MauiApp? Current { get; private set; }

    // Settable by tests via InternalsVisibleTo; null = use the real AppData path.
    internal static string? TestCrashLogPath;

    private static string CrashLogPath =>
        TestCrashLogPath
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PowerMate", "crash.log");

    public static MauiApp CreateMauiApp()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject as Exception, "UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog(e.Exception, "UnobservedTask");
            e.SetObserved();
        };

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton(_ => PowerMateConfig.Load());
        builder.Services.AddSingleton<IHidService, HidService>();
        builder.Services.AddSingleton<IAudioService, AudioService>();
        builder.Services.AddSingleton<IMediaSessionService, MediaSessionService>();
        builder.Services.AddSingleton<PowerMateController>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

        Current = builder.Build();
        return Current;
    }

    internal static void WriteCrashLog(Exception? ex, string source)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath)!;
            Directory.CreateDirectory(dir);
            var entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            File.AppendAllText(CrashLogPath, entry);
        }
        catch { }
    }
}
