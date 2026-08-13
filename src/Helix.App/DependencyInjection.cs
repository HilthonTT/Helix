using Helix.App.Pages.Home;
using Helix.App.Services;
using Helix.Application.Abstractions.Security;
using SharpHook;

namespace Helix.App;

public static class DependencyInjection
{
    public static IServiceCollection AddPresensation(this IServiceCollection services)
    {
        services.AddSingleton<IGlobalHook>(sp => new TaskPoolGlobalHook(runAsyncOnBackgroundThread: true));

        services.AddSingleton<IPassphrasePrompt, PassphrasePromptService>();

        // Owns the reaction to connectivity changes for the app's lifetime.
        services.AddSingleton<DriveWatchdog>();

        services.AddScoped<HomePage>();

        return services;
    }
}
