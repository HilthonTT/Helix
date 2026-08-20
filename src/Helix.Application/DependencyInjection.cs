using Helix.Application.Features.Auditlogs.Commands;
using Helix.Application.Features.Auditlogs.Queries;
using Helix.Application.Features.Diagnostics.Commands;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Updates.Queries;
using Helix.Application.Features.Users.Commands;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services
            .AddAuditlogsHandlers()
            .AddDiagnosticsHandlers()
            .AddDrivesHandlers()
            .AddSettingsHandlers()
            .AddUpdatesHandlers()
            .AddUsersHandlers();

        return services;
    }

    private static IServiceCollection AddAuditlogsHandlers(this IServiceCollection services)
    {
        services.AddScoped<GetAuditlogs>();
        services.AddScoped<PruneAuditlogs>();
        services.AddScoped<SearchAuditlogs>();

        return services;
    }

    private static IServiceCollection AddDiagnosticsHandlers(this IServiceCollection services)
    {
        services.AddScoped<ExportDiagnostics>();

        return services;
    }

    private static IServiceCollection AddDrivesHandlers(this IServiceCollection services)
    {
        services.AddScoped<ConnectAllDrives>();
        services.AddScoped<ConnectDrive>();

        services.AddScoped<CreateDrive>();
        services.AddScoped<DeleteDrive>();
        services.AddScoped<GetAvailableDriveLetters>();
        services.AddScoped<GetDriveById>();
        services.AddScoped<GetDrives>();
        services.AddScoped<SearchDrives>();
        services.AddScoped<UpdateDrive>();

        services.AddScoped<DisconnectDrive>();
        services.AddScoped<DisconnectAllDrives>();
        services.AddScoped<ReconnectDrive>();
        services.AddScoped<TestDriveConnection>();

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

    private static IServiceCollection AddUpdatesHandlers(this IServiceCollection services)
    {
        services.AddScoped<CheckForUpdates>();

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
