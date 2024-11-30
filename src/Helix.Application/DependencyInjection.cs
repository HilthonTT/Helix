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
        services.AddSingleton<GetAuditlogs>();
        services.AddSingleton<SearchAuditlogs>();

        return services;
    }

    private static IServiceCollection AddDrivesHandlers(this IServiceCollection services)
    {
        services.AddSingleton<ConnectAllDrives>();
        services.AddSingleton<ConnectDrive>();

        services.AddSingleton<CreateDrive>();
        services.AddSingleton<DeleteDrive>();
        services.AddSingleton<GetDriveById>();
        services.AddSingleton<GetDrives>();
        services.AddSingleton<SearchDrives>();
        services.AddSingleton<UpdateDrive>();

        services.AddSingleton<DisconnectDrive>();
        services.AddSingleton<DisconnectAllDrives>();

        services.AddSingleton<ExportDrives>();
        services.AddSingleton<ImportDrives>();

        return services;
    }

    private static IServiceCollection AddSettingsHandlers(this IServiceCollection services)
    {
        services.AddSingleton<GetSettings>();
        services.AddSingleton<UpdateSettings>();

        return services;
    }

    private static IServiceCollection AddUsersHandlers(this IServiceCollection services)
    {
        services.AddSingleton<ChangeUserPassword>();
        services.AddSingleton<LoginUser>();
        services.AddSingleton<LogoutUser>();
        services.AddSingleton<RegisterUser>();
        services.AddSingleton<UpdateUser>();

        return services;
    }
}
