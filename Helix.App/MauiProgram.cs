using CommunityToolkit.Maui;
using Helix.Application;
using Helix.Infrastructure;
using Microcharts.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
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
            .AddInfrastructure();

        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(wndLifeCycleBuilder =>
            {
                wndLifeCycleBuilder.OnWindowCreated(window =>
                {
                    IntPtr nativeWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WindowId win32WindowsId = Win32Interop.GetWindowIdFromWindow(nativeWindowHandle);
                    AppWindow winupAppWindow = AppWindow.GetFromWindowId(win32WindowsId);

                    const int height = 800;
                    const int width = 1400;

                    const int x = 1920 / 2 - width / 2;
                    const int y = 1080 / 2 - height / 2;

                    winupAppWindow.MoveAndResize(new RectInt32(x, y, width, height));
                });
            });
        });

        return builder.Build();
    }

    private static void ModifyEntry()
    {
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoMoreBorders", (handler, view) =>
        {
            handler.PlatformView.FontWeight = Microsoft.UI.Text.FontWeights.Thin;
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        });
    }
}
