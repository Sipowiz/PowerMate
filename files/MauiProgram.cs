using PowerMateSettings.Views;

namespace PowerMateSettings;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("SegoeUI-Regular.ttf",    "SegoeUI");
                fonts.AddFont("SegoeUI-SemiBold.ttf",   "SegoeUISemiBold");
            });

        return builder.Build();
    }
}
