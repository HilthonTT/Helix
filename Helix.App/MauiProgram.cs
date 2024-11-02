using CommunityToolkit.Maui;
using Helix.Application;
using Helix.Infrastructure;
using Microcharts.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SharpHook;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Windows.Graphics;

namespace Helix.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        MauiAppBuilder builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .UseMicrocharts()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("fa_solid.ttf", "FontAwesome");
                fonts.AddFont("fabmdl2.ttf", "Fabric");

                fonts.AddFont("SpaceMono-Regular.ttf", "SpaceMonoRegular");
                fonts.AddFont("SpaceMono-Bold.ttf", "SpaceMonoBold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
                ModifyEntry();
            });

#if DEBUG
		builder.Logging.AddDebug();
#endif

        builder.Services
            .AddApplication()
            .AddInfrastructure()
            .AddPresensation();

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(wndLifeCycleBuilder =>
            {
                wndLifeCycleBuilder.OnWindowCreated(window =>
                {
                    IntPtr nativeWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WindowId win32WindowId = Win32Interop.GetWindowIdFromWindow(nativeWindowHandle);
                    AppWindow appWindow = AppWindow.GetFromWindowId(win32WindowId);

                    // Get the screen's current resolution
                    DisplayArea displayArea = DisplayArea.GetFromWindowId(win32WindowId, DisplayAreaFallback.Primary);
                    RectInt32 displayBounds = displayArea.WorkArea;

                    int screenWidth = displayBounds.Width;
                    int screenHeight = displayBounds.Height;

                    // Calculate the window size to maintain a 16:9 aspect ratio
                    const double targetAspectRatio = 16.0 / 9.0;
                    int windowWidth = (int)(screenWidth * 0.8); // 80% of the screen width
                    int windowHeight = (int)(windowWidth / targetAspectRatio);

                    // Ensure the window height fits within the screen's height
                    if (windowHeight > screenHeight * 0.8)
                    {
                        windowHeight = (int)(screenHeight * 0.8);
                        windowWidth = (int)(windowHeight * targetAspectRatio);
                    }

                    // Center the window on the screen
                    int posX = (screenWidth - windowWidth) / 2;
                    int posY = (screenHeight - windowHeight) / 2;

                    appWindow.MoveAndResize(new RectInt32(posX, posY, windowWidth, windowHeight));
                });
            });
        });


        MauiApp app = builder.Build();

        var hook = app.Services.GetRequiredService<IGlobalHook>();

        _ = hook.RunAsync();

        return app;
    }

    private static void ModifyEntry()
    {
        EntryHandler.Mapper.AppendToMapping("NoMoreBorders", (handler, view) =>
        {
            handler.PlatformView.FontWeight = Microsoft.UI.Text.FontWeights.Thin;
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
    }
}
