using Microsoft.Extensions.Logging;
using PowerMate.Models;
using PowerMate.Services;
using PowerMate.ViewModels;
using PowerMate.Views;

namespace PowerMate;

public static class MauiProgram
{
    public static MauiApp? Current { get; private set; }

    public static MauiApp CreateMauiApp()
    {
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
        builder.Services.AddSingleton<PowerMateController>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<SettingsPage>();

        Current = builder.Build();
        return Current;
    }
}
