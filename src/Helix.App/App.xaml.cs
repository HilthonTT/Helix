using Helix.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using AppBase = Microsoft.Maui.Controls.Application;
using Helix.App.Resources.Languages;
using Helix.App.Services;

namespace Helix.App;

public sealed partial class App : AppBase
{
    public static IServiceProvider ServiceProvider { get; private set; } = default!;

    public App(IServiceProvider serviceProvider)
    {
        InitializeComponent();

        ServiceProvider = serviceProvider;

        RegisterGlobalExceptionHandlers();

        ILogger<App> logger = serviceProvider.GetRequiredService<ILogger<App>>();

        try
        {
            using IServiceScope scope = serviceProvider.CreateScope();

            DatabaseInitializer.Initialize(scope.ServiceProvider.GetRequiredService<AppDbContext>(), logger);
        }
        catch (Exception ex)
        {
            StartupFailure.Exit(logger, ex);
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.For<App>().LogCritical("Unhandled domain exception: {Exception}", e.ExceptionObject);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.For<App>().LogError(e.Exception, "Unobserved task exception.");
            e.SetObserved();
        };

#if WINDOWS
        Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
        {
            AppLog.For<App>().LogError(e.Exception, "Unhandled WinUI exception; the app was kept alive.");

            e.Handled = true;

            Notifier.Error(e.Exception?.Message ?? AppResources.UnexpectedError);
        };
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

#if MACCATALYST
        DisplayInfo display = DeviceDisplay.Current.MainDisplayInfo;
        double density = display.Density > 0 ? display.Density : 1;

        WindowBounds bounds = WindowSizing.Calculate(
            (int)(display.Width / density),
            (int)(display.Height / density));

        window.X = bounds.X;
        window.Y = bounds.Y;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
#endif

        return window;
    }
}
