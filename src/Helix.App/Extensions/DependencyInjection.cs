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

        services.AddScoped<HomePage>();

        return services;
    }
}
