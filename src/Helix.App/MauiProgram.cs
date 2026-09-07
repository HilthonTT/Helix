using CommunityToolkit.Maui;
using Helix.App.Extensions;
using Helix.Application;
using Helix.Infrastructure;
using Helix.Infrastructure.Cryptography;
using Microcharts.Maui;
using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;
using SkiaSharp.Views.Maui.Controls.Hosting;
#if WINDOWS
using Helix.App.Services;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using Windows.Graphics;
#endif

namespace Helix.App;

public static class MauiProgram
{
#if WINDOWS
    private static Mutex? _singleInstance;
#endif

    public static MauiApp CreateMauiApp()
    {
#if WINDOWS
        ExitIfAlreadyRunning();
#endif

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
        builder.ConfigureLifecycleEvents(events =>
        {
            events.AddWindows(wndLifeCycleBuilder =>
            {
                wndLifeCycleBuilder.OnWindowCreated(window =>
                {
                    IntPtr nativeWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WindowId win32WindowId = Win32Interop.GetWindowIdFromWindow(nativeWindowHandle);
                    AppWindow appWindow = AppWindow.GetFromWindowId(win32WindowId);

                    DisplayArea displayArea = DisplayArea.GetFromWindowId(win32WindowId, DisplayAreaFallback.Primary);
                    RectInt32 displayBounds = displayArea.WorkArea;

                    WindowBounds bounds = WindowSizing.Calculate(displayBounds.Width, displayBounds.Height);

                    appWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));

                    appWindow.Closing += OnWindowClosing;
                });
            });
        });
#endif

        MauiApp app = builder.Build();

        ILogger startupLogger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Helix.Startup");

        try
        {
            Task.Run(() => PasswordGenerator.InitializeAsync(startupLogger)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            StartupFailure.Exit(startupLogger, ex);
        }

        var hook = app.Services.GetRequiredService<IGlobalHook>();

        hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true).ContinueWith(
            t => startupLogger.LogError(t.Exception, "The global keyboard hook faulted; the Ctrl+Enter shortcut is dead for this session. {Hint}", HookFailureHint),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return app;
    }

    private const string HookFailureHint =
#if MACCATALYST
        "Grant Helix access under System Settings > Privacy & Security > Accessibility to enable it.";
#else
        "";
#endif

#if WINDOWS
    private static void ExitIfAlreadyRunning()
    {
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Helix.App.SingleInstance", out bool createdNew);

        if (createdNew)
        {
            return;
        }

        try
        {
            Process current = Process.GetCurrentProcess();

            foreach (Process other in Process.GetProcessesByName(current.ProcessName))
            {
                if (other.Id != current.Id && other.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(other.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
        }
        catch (Exception)
        {
        }

        Environment.Exit(0);
    }

    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private static void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (MainWindow.IsExiting)
        {
            return;
        }

        TrayIconService? tray = App.ServiceProvider?.GetService<TrayIconService>();
        if (tray is null || !tray.IsRunning)
        {
            return;
        }

        args.Cancel = true;

        if (!tray.ClosesToTray)
        {
            tray.Stop();
            MainWindow.Exit();

            return;
        }

        MainWindow.HideToTray();
        tray.NotifyHiddenToTray();
    }

    private static void ModifyEntry()
    {
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
