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

        services.AddScoped<HomePage>();

        return services;
    }
}
