using Microsoft.Extensions.Logging;
using AirPlayStreamer.Services;

namespace AirPlayStreamer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            })
            .RegisterServices();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static MauiAppBuilder RegisterServices(this MauiAppBuilder builder)
    {
#if IOS
        builder.Services.AddSingleton<IAirPlayService, Platforms.iOS.Services.AppleAirPlayService>();
#elif MACCATALYST
        builder.Services.AddSingleton<IAirPlayService, Platforms.MacCatalyst.Services.AppleAirPlayService>();
#elif ANDROID
        builder.Services.AddSingleton<IAirPlayService, Platforms.Android.Services.AndroidAirPlayService>();
#elif WINDOWS
        builder.Services.AddSingleton<IAirPlayService, Platforms.Windows.Services.WindowsAirPlayService>();
#endif

        builder.Services.AddTransient<MainPage>();

        return builder;
    }
}
