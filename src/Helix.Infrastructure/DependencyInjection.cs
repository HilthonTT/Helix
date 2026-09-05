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

    /// <summary>
    /// Wires the file log up. Registered as an <see cref="ILoggerProvider"/> so it joins
    /// whatever else the host has configured rather than replacing it — the Debug
    /// provider still runs alongside it under a debugger.
    /// </summary>
    private static IServiceCollection AddDiagnostics(this IServiceCollection services)
    {
        // Resolved lazily: FileSystem.AppDataDirectory needs MAUI to be initialized,
        // which it is not yet while the container is being described.
        services.AddSingleton(_ => new LogFileWriter(
            () => DiagnosticsConfiguration.LogDirectory,
            DiagnosticsConfiguration.RetainedDays));

        services.AddSingleton<IDiagnosticsLog, DiagnosticsLog>();

        services.AddSingleton<ILoggerProvider>(sp => new FileLoggerProvider(
            sp.GetRequiredService<LogFileWriter>(),
#if DEBUG
            LogLevel.Debug));
#else
            // Information and up in a release build. Debug-level lines are for someone
            // stepping through, and writing them to a file the user may send on is how a
            // log ends up carrying more than it should.
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
            // Order matters: the audit-log interceptor adds Auditlog rows during the
            // save, and those rows still need their timestamps stamped afterwards.
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

        // Not per-OS: the shell verb that opens a folder is the same call on both heads.
        services.AddSingleton<IFileBrowser, FileBrowser>();
        services.AddSingleton<ICountdownService, CountdownService>();

        // Holds the watched set and the polling loop for the app's lifetime.
        services.AddSingleton<IDriveMonitor, DriveMonitor>();

        // Singleton for its cache: the reconnect loop asks about the same handful of
        // hosts over and over, and several mapped drives are usually one NAS.
        services.AddSingleton<IHostReachability, HostReachability>();

        // One client for the app's lifetime, as HttpClient is meant to be used. The
        // current version is read through a delegate so the checker stays testable
        // without a MAUI host behind AppInfo.
        services.AddSingleton<IUpdateChecker>(sp => new GitHubUpdateChecker(
            UpdateConfiguration.CreateHttpClient(),
            sp.GetRequiredService<ILogger<GitHubUpdateChecker>>(),
            () => AppInfo.Current.VersionString,
            () => UpdateConfiguration.AssetMonikers));

        // Its own client, with a timeout that suits moving a release rather than reading
        // one. Both paths are resolved through delegates so a test can point the swap at
        // a temporary folder instead of at the running app.
        services.AddSingleton<IUpdateInstaller>(sp => new UpdateInstaller(
            UpdateConfiguration.CreateDownloadHttpClient(),
            sp.GetRequiredService<ILogger<UpdateInstaller>>(),
            () => UpdateInstaller.DefaultInstallDirectory,
            () => UpdateInstaller.DefaultStagingRoot,
            () => DiagnosticsConfiguration.LogDirectory));

        services.AddPlatformServices();

        return services;
    }

    /// <summary>
    /// Binds the six abstractions whose implementation is genuinely per-OS: mounting a
    /// share, registering for launch at login, putting a shortcut on the desktop, sitting
    /// in the system tray, measuring what a mount is really on, and asking the system how
    /// long it has been since anyone touched it.
    /// Everything else in this layer is platform-neutral.
    /// </summary>
    private static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
#if WINDOWS
        // Stateless and cached in viewmodel fields — must survive per-operation scopes.
        services.AddSingleton<INasConnector, WindowsNasConnector>();

        services.AddScoped<IStartupService, WindowsStartupService>();
        services.AddScoped<IDesktopService, WindowsDesktopService>();

        // Owns a window and a thread for the app's lifetime, so it can only be a singleton.
        services.AddSingleton<ITrayIcon, WindowsTrayIcon>();

        services.AddSingleton<IStorageProbe, WindowsStorageProbe>();

        services.AddSingleton<IIdleTimeProvider, WindowsIdleTimeProvider>();
#elif MACCATALYST
        services.AddSingleton<INasConnector, MacNasConnector>();

        services.AddScoped<IStartupService, MacStartupService>();
        services.AddScoped<IDesktopService, MacDesktopService>();

        // No menu-bar status item from a Catalyst process; the callers check IsSupported.
        services.AddSingleton<ITrayIcon, UnsupportedTrayIcon>();

        services.AddSingleton<IStorageProbe, MacStorageProbe>();

        services.AddSingleton<IIdleTimeProvider, MacIdleTimeProvider>();
#else
        // Fail at composition rather than at the first drive connection: a head added
        // without its platform services would otherwise look fine until it was used.
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

        // Holds the app-wide login state — must be shared across the per-operation
        // scopes the presentation layer creates for each handler invocation.
        services.AddSingleton<ILoggedInUser, LoggedInUser>();

        services.AddSingleton<IVaultCipher, VaultCipher>();

        return services;
    }
}
