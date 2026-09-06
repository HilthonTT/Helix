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
    /// <summary>
    /// Held for the life of the process, so a second copy can tell there is a first.
    /// </summary>
    /// <remarks>
    /// Two Helixes on one database is what the update helper produced when it gave up
    /// waiting for the old one to exit and started the new one anyway, and what a user
    /// gets from double-clicking a shortcut while the first is hidden in the tray. The
    /// second would open the same SQLCipher file, put up a second tray icon and run a
    /// second watchdog against the same letters. It brings the first one's window forward
    /// where it can, and goes away.
    /// </remarks>
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

                    // The close button parks the app in the tray instead of ending the
                    // session; the tray's Exit item is what ends it.
                    appWindow.Closing += OnWindowClosing;
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
        try
        {
            Task.Run(() => PasswordGenerator.InitializeAsync(startupLogger)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // The generator refuses to make a key while a database exists that it cannot
            // read - the alternative was overwriting the real key. Said out loud, since
            // there is no window yet to say it in.
            StartupFailure.Exit(startupLogger, ex);
        }

        // The global hook backs the Ctrl+Enter shortcut on the sign-in pages. It runs on
        // both heads as of SharpHook 8, which ships a Mac Catalyst assembly and the
        // libuiohook natives to go with it.
        var hook = app.Services.GetRequiredService<IGlobalHook>();

        // Keyboard only. Ctrl+Enter is the whole of what this is for, and the mouse half
        // of the hook would put every pointer move across the native boundary and onto
        // the task pool for nothing - which on macOS is also half of what the
        // Accessibility permission would be spent on.
        //
        // Single fire-and-forget launch; observe faults so they are not silently
        // swallowed. The background thread is asked for here rather than at
        // construction, which is where SharpHook moved the choice in 8.0: the native
        // hook loop is blocking, and a foreground thread would keep the process alive
        // after the window has gone. macOS needs the main run loop for this, which a
        // MAUI app has; what it may not have is the Accessibility permission, and
        // without it the hook faults here rather than anywhere the user is waiting.
        hook.RunAsync(GlobalHookType.Keyboard, useBackgroundThread: true).ContinueWith(
            t => startupLogger.LogError(t.Exception, "The global keyboard hook faulted; the Ctrl+Enter shortcut is dead for this session. {Hint}", HookFailureHint),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return app;
    }

    /// <summary>
    /// What to try, for the one platform where a failed hook is something the user can
    /// do something about.
    /// </summary>
    /// <remarks>
    /// macOS refuses a global hook until the app is granted Accessibility access, and
    /// that is by far the likeliest reason for this to fail there - so the log line says
    /// so rather than leaving whoever reads it to guess. Windows has no equivalent
    /// switch, so it gets nothing to chase.
    /// </remarks>
    private const string HookFailureHint =
#if MACCATALYST
        "Grant Helix access under System Settings > Privacy & Security > Accessibility to enable it.";
#else
        "";
#endif

#if WINDOWS
    private static void ExitIfAlreadyRunning()
    {
        // Local\ rather than Global\: one per logon session, which is also how the
        // database and the mapped letters are scoped.
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
            // Best effort: the running copy may be hidden in the tray with no main window
            // to bring forward, and that is still not a reason to start a second one.
        }

        Environment.Exit(0);
    }

    private const int SW_RESTORE = 9;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Turns the title bar's close button into hide-to-tray, where the user asked for
    /// that.
    /// </summary>
    /// <remarks>
    /// Cancelled only while there is a tray icon to come back from — before sign-in, or
    /// where Explorer refused the icon, the close button still closes, because hiding a
    /// window with no way back is worse than quitting. Same rule the auto-minimize
    /// countdown follows in <c>BaseViewModel.MinimizeApp</c>.
    ///
    /// The other way out is <c>Settings.CloseToTray</c> turned off, which is the user
    /// saying the close button means closed. That still goes through
    /// <see cref="MainWindow.Exit"/> rather than simply letting the close through: it is
    /// the app's one real quit, and it takes the icon down on the way — an icon whose
    /// process has gone sits in the tray until somebody happens to mouse over it.
    ///
    /// This also fires for the shutdown that quit asks for, hence the flag: without it
    /// Exit would hide the window instead of closing it.
    /// </remarks>
    private static void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (MainWindow.IsExiting)
        {
            return;
        }

        // GetService, not GetRequiredService: nothing about closing a window should be
        // able to throw out of a WinUI event handler.
        TrayIconService? tray = App.ServiceProvider?.GetService<TrayIconService>();
        if (tray is null || !tray.IsRunning)
        {
            return;
        }

        // Cancelled either way: the quit below closes this window itself, and letting
        // this close run on as well would race it.
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
