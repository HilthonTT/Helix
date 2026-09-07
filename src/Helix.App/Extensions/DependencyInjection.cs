using Helix.App.Services;
using Helix.App.Views.Drives;
using Helix.Application.Abstractions.Security;
using SharpHook;

namespace Helix.App.Extensions;

public static class DependencyInjection
{
    public static IServiceCollection AddPresensation(this IServiceCollection services)
    {
        services.AddSingleton<IGlobalHook>(sp => new TaskPoolGlobalHook());

        services.AddSingleton<IPassphrasePrompt, PassphrasePromptService>();

        services.AddSingleton<DriveWatchdog>();

        services.AddSingleton<TrayIconService>();

        services.AddSingleton<StorageAlertService>();

        services.AddSingleton<IdleLockService>();

        services.AddScoped<HomePage>();

        return services;
    }
}
