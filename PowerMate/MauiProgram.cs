using Microsoft.Extensions.Logging;
using PowerMate.Models;
using PowerMate.Services;
using PowerMate.ViewModels;
using PowerMate.Views;
using Serilog;

namespace PowerMate;

public static class MauiProgram
{
    public static MauiApp? Current { get; private set; }

    // Settable by tests to redirect the log file; null = use %AppData%\PowerMate
    internal static string? TestCrashLogPath;

    private static string ProductionLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PowerMate", "crash-.log");

    internal static void ConfigureLogging()
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                TestCrashLogPath ?? ProductionLogPath,
                rollingInterval: TestCrashLogPath != null
                    ? RollingInterval.Infinite
                    : RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
                buffered: false);

        Log.Logger = config.CreateLogger();
    }

    public static MauiApp CreateMauiApp()
    {
        ConfigureLogging();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Fatal(e.ExceptionObject as Exception, "[{Source}]", "UnhandledException");
            Log.CloseAndFlush();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "[{Source}]", "UnobservedTask");
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
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

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

    internal static void WriteCrashLog(Exception? ex, string source) =>
        Log.Fatal(ex, "[{Source}]", source);
}
