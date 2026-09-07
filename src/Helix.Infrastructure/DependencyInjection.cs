using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Diagnostics;
using Helix.Application.Abstractions.Security;
using Helix.Application.Abstractions.Startup;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Abstractions.Updates;
using Helix.Application.Abstractions.Time;
using Helix.Domain.Auditlogs;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using Helix.Infrastructure.Authentication;
using Helix.Infrastructure.Connector;
using Helix.Infrastructure.Cryptography;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Interceptors;
using Helix.Infrastructure.Database.Repositories;
using Helix.Infrastructure.Desktop;
using Helix.Infrastructure.Diagnostics;
using Helix.Infrastructure.Startup;
using Helix.Infrastructure.Storage;
using Helix.Infrastructure.Time;
using Helix.Infrastructure.Updates;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services
            .AddServices()
            .AddDiagnostics()
            .AddDatabase()
            .AddAuthenticationInternal();

        return services;
    }

    private static IServiceCollection AddDiagnostics(this IServiceCollection services)
    {
        services.AddSingleton(_ => new LogFileWriter(
            () => DiagnosticsConfiguration.LogDirectory,
            DiagnosticsConfiguration.RetainedDays));

        services.AddSingleton<IDiagnosticsLog, DiagnosticsLog>();

        services.AddLogging(logging =>
            logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning));

        services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(
            sp.GetRequiredService<LogFileWriter>(),
#if DEBUG
            LogLevel.Debug));
#else
            LogLevel.Information));
#endif

        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        services.AddSingleton<InsertAuditLogsInterceptor>();
        services.AddSingleton<UpdateAuditableEntitiesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(
                sp.GetRequiredService<InsertAuditLogsInterceptor>(),
                sp.GetRequiredService<UpdateAuditableEntitiesInterceptor>());
        });

        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<IDriveRepository, DriveRepository>();

        services.AddScoped<IDriveGroupRepository, DriveGroupRepository>();

        services.AddScoped<ISettingsRepository, SettingsRepository>();

        services.AddScoped<IAuditlogRepository, AuditlogRepository>();

        return services;
    }

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddSingleton<IFileBrowser, FileBrowser>();
        services.AddSingleton<ICountdownService, CountdownService>();

        services.AddSingleton<IDriveMonitor, DriveMonitor>();

        services.AddSingleton<IHostReachability, HostReachability>();

        services.AddSingleton<IHostDiagnostics, HostDiagnostics>();

        services.AddSingleton<IUpdateChecker>(sp => new GitHubUpdateChecker(
            UpdateConfiguration.CreateHttpClient(),
            sp.GetRequiredService<ILogger<GitHubUpdateChecker>>(),
            () => AppInfo.Current.VersionString,
            () => UpdateConfiguration.AssetMonikers));

        services.AddSingleton<IUpdateInstaller>(sp => new UpdateInstaller(
            UpdateConfiguration.CreateDownloadHttpClient(),
            sp.GetRequiredService<ILogger<UpdateInstaller>>(),
            () => UpdateInstaller.DefaultInstallDirectory,
            () => UpdateInstaller.DefaultStagingRoot,
            () => DiagnosticsConfiguration.LogDirectory));

        services.AddPlatformServices();

        return services;
    }

    private static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
#if WINDOWS
        services.AddSingleton<INasConnector, WindowsNasConnector>();

        services.AddScoped<IStartupService, WindowsStartupService>();
        services.AddScoped<IDesktopService, WindowsDesktopService>();

        services.AddSingleton<ITrayIcon, WindowsTrayIcon>();

        services.AddSingleton<IStorageProbe, WindowsStorageProbe>();

        services.AddSingleton<IIdleTimeProvider, WindowsIdleTimeProvider>();
#elif MACCATALYST
        services.AddSingleton<INasConnector, MacNasConnector>();

        services.AddScoped<IStartupService, MacStartupService>();
        services.AddScoped<IDesktopService, MacDesktopService>();

        services.AddSingleton<ITrayIcon, UnsupportedTrayIcon>();

        services.AddSingleton<IStorageProbe, MacStorageProbe>();

        services.AddSingleton<IIdleTimeProvider, MacIdleTimeProvider>();
#else
        throw new PlatformNotSupportedException(
            "Helix has no platform services for this target framework. Add implementations of " +
            $"{nameof(INasConnector)}, {nameof(IStartupService)}, {nameof(IDesktopService)}, " +
            $"{nameof(ITrayIcon)}, {nameof(IStorageProbe)} and {nameof(IIdleTimeProvider)} for it.");
#endif

        return services;
    }

    private static IServiceCollection AddAuthenticationInternal(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        services.AddSingleton<ILoggedInUser, LoggedInUser>();

        services.AddSingleton<IVaultCipher, VaultCipher>();

        return services;
    }
}
