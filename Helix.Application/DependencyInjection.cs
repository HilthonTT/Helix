using Helix.Application.Users;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddUserEndpoints();

        return services;
    }

    private static IServiceCollection AddUserEndpoints(this IServiceCollection services)
    {
        services.AddScoped<RegisterUser>();

        return services;
    }
}
