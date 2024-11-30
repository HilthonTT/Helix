using Helix.App.Pages.Home;
using SharpHook;

namespace Helix.App;

public static class DependencyInjection
{
    public static IServiceCollection AddPresensation(this IServiceCollection services)
    {
        services.AddSingleton<IGlobalHook>(sp => new TaskPoolGlobalHook(runAsyncOnBackgroundThread: true));

        services.AddScoped<HomePage>();

        return services;
    }
}
