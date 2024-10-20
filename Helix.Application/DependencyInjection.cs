using Helix.Application.Drives;
using Helix.Application.Users;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services
            .AddUserEndpoints()
            .AddDriveEndpoints();

        return services;
    }

    private static IServiceCollection AddUserEndpoints(this IServiceCollection services)
    {
        services.AddScoped<RegisterUser>();
        services.AddScoped<LoginUser>();
        services.AddScoped<LogoutUser>();

        return services;
    }

    private static IServiceCollection AddDriveEndpoints(this IServiceCollection services)
    {
        services.AddScoped<CreateDrive>();
        services.AddScoped<GetDrives>();

        return services;
    }
}
