using Helix.Application.Features.Auditlogs.Commands;
using Helix.Application.Features.Auditlogs.Queries;
using Helix.Application.Features.Diagnostics.Commands;
using Helix.Application.Features.DriveGroups.Commands;
using Helix.Application.Features.DriveGroups.Queries;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Settings.Commands;
using Helix.Application.Features.Settings.Queries;
using Helix.Application.Features.Storage.Queries;
using Helix.Application.Features.Updates.Commands;
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
            .AddDriveGroupsHandlers()
            .AddDrivesHandlers()
            .AddSettingsHandlers()
            .AddStorageHandlers()
            .AddUpdatesHandlers()
            .AddUsersHandlers();

        return services;
    }

    private static IServiceCollection AddAuditlogsHandlers(this IServiceCollection services)
    {
        services.AddScoped<GetAuditlogs>();
        services.AddScoped<PruneAuditlogs>();

        return services;
    }

    private static IServiceCollection AddDiagnosticsHandlers(this IServiceCollection services)
    {
        services.AddScoped<ExportDiagnostics>();

        return services;
    }

    private static IServiceCollection AddDriveGroupsHandlers(this IServiceCollection services)
    {
        services.AddScoped<ConnectDriveGroup>();
        services.AddScoped<CreateDriveGroup>();
        services.AddScoped<DeleteDriveGroup>();
        services.AddScoped<GetDriveGroupById>();
        services.AddScoped<GetDriveGroups>();
        services.AddScoped<UpdateDriveGroup>();

        return services;
    }

    private static IServiceCollection AddDrivesHandlers(this IServiceCollection services)
    {
        services.AddScoped<ConnectAllDrives>();
        services.AddScoped<ConnectDrive>();
        services.AddScoped<ConnectDrives>();

        services.AddScoped<CreateDrive>();
        services.AddScoped<DeleteDrive>();
        services.AddScoped<DiagnoseDrive>();
        services.AddScoped<GetAvailableDriveLetters>();
        services.AddScoped<GetDriveById>();
        services.AddScoped<GetDrives>();
        services.AddScoped<MarkDriveConnected>();
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

    private static IServiceCollection AddStorageHandlers(this IServiceCollection services)
    {
        services.AddScoped<GetStorageAlerts>();

        return services;
    }

    private static IServiceCollection AddUpdatesHandlers(this IServiceCollection services)
    {
        services.AddScoped<ApplyUpdate>();
        services.AddScoped<CheckForUpdates>();
        services.AddScoped<StageUpdate>();

        return services;
    }

    private static IServiceCollection AddUsersHandlers(this IServiceCollection services)
    {
        services.AddScoped<ChangeUserPassword>();
        services.AddScoped<LoginUser>();
        services.AddScoped<LogoutUser>();
        services.AddScoped<RegisterUser>();
        services.AddScoped<UnlockSession>();
        services.AddScoped<UpdateUser>();

        return services;
    }
}
