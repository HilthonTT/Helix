using Helix.App.Services;
using Helix.App.Views.Drives;
using Helix.Application.Abstractions.Security;
#if WINDOWS
using SharpHook;
#endif

namespace Helix.App.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddPresensation(this IServiceCollection services)
    {
#if WINDOWS
        // Backs the Ctrl+Enter shortcut on the sign-in pages. Windows-only: SharpHook
        // ships no maccatalyst native, so the Catalyst head never resolves this.
        services.AddSingleton<IGlobalHook>(sp => new TaskPoolGlobalHook(runAsyncOnBackgroundThread: true));
#endif

        services.AddSingleton<IPassphrasePrompt, PassphrasePromptService>();

        // Owns the reaction to connectivity changes for the app's lifetime.
        services.AddSingleton<DriveWatchdog>();

        // Owns the tray icon's menu and the commands behind it, likewise for the app's
        // lifetime — the icon it drives is a singleton and outlives every page.
        services.AddSingleton<TrayIconService>();

        // Holds its own slow timer and the set of volumes already warned about, so like
        // the two above it lives as long as the app does.
        services.AddSingleton<StorageAlertService>();

        // Holds the idle poll and the route to come back to, so it outlives every page
        // it might lock.
        services.AddSingleton<IdleLockService>();

        services.AddScoped<HomePage>();

        return services;
    }
}
