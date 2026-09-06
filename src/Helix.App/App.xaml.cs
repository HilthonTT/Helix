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

        using IServiceScope scope = serviceProvider.CreateScope();

        DatabaseInitializer.Initialize(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            serviceProvider.GetRequiredService<ILogger<App>>());
    }

    /// <summary>
    /// Last-resort safety net. Without these hooks any exception that escapes an
    /// <c>async void</c> handler, a background task, or the WinUI dispatcher tears the
    /// whole process down. Here we log every fault and, for the WinUI UI thread,
    /// mark it handled so the app stays alive and shows an alert instead of crashing.
    /// </summary>
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

            // Keep the app alive; surface the failure without killing the process.
            e.Handled = true;

            // Notifier holds this until a page with a banner host is on screen, which
            // matters here more than anywhere: a fault during startup or navigation used
            // to have no window to raise an alert on and was reported to nobody.
            Notifier.Error(e.Exception?.Message ?? AppResources.UnexpectedError);
        };
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell());

#if MACCATALYST
        // Windows sizes its window through AppWindow in a lifecycle event; Catalyst has
        // no equivalent, so the same WindowSizing rule is applied to MAUI's own window
        // geometry. DisplayInfo reports physical pixels and these properties take
        // device-independent units, hence the density divide.
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
