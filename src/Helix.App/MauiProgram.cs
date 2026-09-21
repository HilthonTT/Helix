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
using System.IO.Pipes;
using System.Text;
using Windows.Graphics;
#endif

namespace Helix.App;

public static class MauiProgram
{
#if WINDOWS
    private static Mutex? _singleInstance;

    private const string SingleInstanceMutexName = @"Local\Helix.App.SingleInstance";

    private const int CommandConnectTimeoutMilliseconds = 3_000;
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

#if WINDOWS
        app.Services.GetRequiredService<CommandListener>().Start();
#endif

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
        CommandRequest request = CommandRequest.Parse([.. Environment.GetCommandLineArgs().Skip(1)]);

        if (request.Verb == CommandVerb.Help)
        {
            ExitWith(CommandListener.Success + CommandRequest.Usage);
        }

        if (request.Verb == CommandVerb.Unknown)
        {
            ExitWith($"{CommandListener.Failure}'{request.Target}' is not something Helix understands.{CommandRequest.LineBreak}{CommandRequest.LineBreak}{CommandRequest.Usage}");
        }

        _singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);

        if (createdNew)
        {
            if (request.Verb != CommandVerb.None)
            {
                ExitWith(CommandListener.Failure + "Helix is not running. Start it and sign in, then try again.");
            }

            return;
        }

        if (request.Verb is CommandVerb.None or CommandVerb.Show)
        {
            BringOtherInstanceForward();
        }

        string? reply = Send(request);

        if (request.Verb == CommandVerb.None)
        {
            Environment.Exit(0);
        }

        ExitWith(reply ?? CommandListener.Failure + "Helix is running but did not answer.");
    }

    private static void BringOtherInstanceForward()
    {
        try
        {
            Process current = Process.GetCurrentProcess();

            foreach (Process other in Process.GetProcessesByName(current.ProcessName))
            {
                if (other.Id == current.Id)
                {
                    continue;
                }

                AllowSetForegroundWindow(other.Id);

                if (other.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(other.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(other.MainWindowHandle);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static string? Send(CommandRequest request)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                CommandListener.PipeName,
                PipeDirection.InOut,
                PipeOptions.CurrentUserOnly);

            client.Connect(CommandConnectTimeoutMilliseconds);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            writer.WriteLine(request.Encode());

            return reader.ReadLine();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void ExitWith(string reply)
    {
        bool ok = reply.StartsWith(CommandListener.Success, StringComparison.Ordinal);
        string message = reply.Length > 0 ? reply[1..] : string.Empty;

        if (message.Length > 0)
        {
            try
            {
                AttachConsole(AttachParentProcess);

                using var output = new StreamWriter(ok ? Console.OpenStandardOutput() : Console.OpenStandardError())
                {
                    AutoFlush = true,
                };

                output.WriteLine();
                output.WriteLine(message.Replace(CommandRequest.LineBreak.ToString(), Environment.NewLine));
            }
            catch (Exception)
            {
            }
        }

        Environment.Exit(ok ? 0 : 1);
    }

    private const int AttachParentProcess = -1;

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

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
