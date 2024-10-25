using Helix.Application.Drives;
using Helix.Application.Settings;
using Helix.Application.Users;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services
            .AddUserEndpoints()
            .AddDriveEndpoints()
            .AddSettingsEndpoint();

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
        services.AddScoped<DeleteDrive>();
        services.AddScoped<UpdateDrive>();
        services.AddScoped<GetDriveById>();
        
        services.AddScoped<ExportDrives>();
        services.AddScoped<ImportDrives>();

        services.AddScoped<ConnectDrive>();
        services.AddScoped<DisconnectDrive>();

        return services;
    }

    private static IServiceCollection AddSettingsEndpoint(this IServiceCollection services)
    {
        services.AddScoped<GetSettings>();
        services.AddScoped<UpdateSettings>();

        return services;
    }
}
