using Helix.Application.Auditlogs;
using Helix.Application.Drives;
using Helix.Application.Settings;
using Helix.Application.Users;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services
            .AddAuditlogsHandlers()
            .AddDrivesHandlers()
            .AddSettingsHandlers()
            .AddUsersHandlers();

        return services;
    }

    private static IServiceCollection AddAuditlogsHandlers(this IServiceCollection services)
    {
        services.AddScoped<GetAuditlogs>();
        services.AddScoped<SearchAuditlogs>();

        return services;
    }

    private static IServiceCollection AddDrivesHandlers(this IServiceCollection services)
    {
        services.AddScoped<ConnectAllDrives>();
        services.AddScoped<ConnectDrive>();

        services.AddScoped<CreateDrive>();
        services.AddScoped<DeleteDrive>();
        services.AddScoped<GetDriveById>();
        services.AddScoped<GetDrives>();
        services.AddScoped<SearchDrives>();
        services.AddScoped<UpdateDrive>();

        services.AddScoped<DisconnectDrive>();
        services.AddScoped<DisconnectAllDrives>();

        services.AddScoped<ExportDrives>();
        services.AddScoped<ImportDrives>();

        return services;
    }

    private static IServiceCollection AddSettingsHandlers(this IServiceCollection services)
    {
        services.AddScoped<GetSettings>();
        services.AddScoped<UpdateSettings>();

        return services;
    }

    private static IServiceCollection AddUsersHandlers(this IServiceCollection services)
    {
        services.AddScoped<ChangeUserPassword>();
        services.AddScoped<LoginUser>();
        services.AddScoped<LogoutUser>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<UpdateUser>();

        return services;
    }
}
