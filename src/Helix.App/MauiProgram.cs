using CommunityToolkit.Maui;
using Helix.App.Extensions;
using Helix.Application;
using Helix.Infrastructure;
using Helix.Infrastructure.Cryptography;
using Microcharts.Maui;
using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using SharpHook;
using System.Diagnostics;
using Windows.Graphics;
#endif

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

                fonts.AddFont("SpaceMono-Regular.ttf", "SpaceMonoRegular");
                fonts.AddFont("SpaceMono-Bold.ttf", "SpaceMonoBold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if WINDOWS
                ModifyEntry();
#endif
            });

#if DEBUG
		builder.Logging.AddDebug();
#endif

        builder.Services
            .AddApplication()
            .AddInfrastructure()
            .AddPresensation();

#if WINDOWS
        // Catalyst has no AppWindow/DisplayArea; it sizes its window in App.CreateWindow
        // from the same WindowSizing rule.
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

                    WindowBounds bounds = WindowSizing.Calculate(displayBounds.Width, displayBounds.Height);

                    appWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
                });
            });
        });
#endif

        MauiApp app = builder.Build();

        ILogger startupLogger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Helix.Startup");

        // Resolve the SQLCipher key from SecureStorage on a background thread before
        // any DbContext is constructed. Wrapped in Task.Run so no UI/MAUI sync context
        // is captured by the underlying SecureStorage call.
        Task.Run(() => PasswordGenerator.InitializeAsync(startupLogger)).GetAwaiter().GetResult();

#if WINDOWS
        // The global hook backs the Ctrl+Enter shortcut on the sign-in pages. libuiohook
        // ships no maccatalyst native and macOS would gate it behind an Accessibility
        // prompt, so the Catalyst head goes without it.
        var hook = app.Services.GetRequiredService<IGlobalHook>();

        // Single fire-and-forget launch of the global hook; observe faults so they
        // are not silently swallowed.
        hook.RunAsync().ContinueWith(
            t => startupLogger.LogError(t.Exception, "The global keyboard hook faulted; the Ctrl+Enter shortcut is dead for this session."),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
#endif

        return app;
    }

#if WINDOWS
    private static void ModifyEntry()
    {
        // Entries sit inside our own bordered Field container, so the platform chrome
        // is removed. Weight stays Normal — the previous Thin made input text noticeably
        // lighter than every label beside it.
        EntryHandler.Mapper.AppendToMapping("HelixEntryChrome", (handler, view) =>
        {
            handler.PlatformView.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
            handler.PlatformView.Background = null;
            handler.PlatformView.Padding = new Microsoft.UI.Xaml.Thickness(0);
        });
    }
#endif
}
