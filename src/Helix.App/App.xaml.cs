using Helix.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using AppBase = Microsoft.Maui.Controls.Application;

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
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
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
            Debug.WriteLine($"Helix: unhandled domain exception: {e.ExceptionObject}");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Debug.WriteLine($"Helix: unobserved task exception: {e.Exception}");
            e.SetObserved();
        };

#if WINDOWS
        Microsoft.UI.Xaml.Application.Current.UnhandledException += (_, e) =>
        {
            Debug.WriteLine($"Helix: unhandled WinUI exception: {e.Exception}");

            // Keep the app alive; surface the failure without killing the process.
            e.Handled = true;

            string message = e.Exception?.Message ?? "An unexpected error occurred.";
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    Page? page = Current?.Windows.Count > 0 ? Current.Windows[0].Page : null;
                    if (page is not null)
                    {
                        await page.DisplayAlert("Something went wrong!", message, "Ok");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Helix: failed to show crash alert: {ex}");
                }
            });
        };
#endif
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell());
    }
}
