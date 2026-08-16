using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Security;
using Helix.Application.Abstractions.Startup;
using Helix.Application.Abstractions.Time;
using Helix.Domain.Auditlogs;
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
using Helix.Infrastructure.Startup;
using Helix.Infrastructure.Time;

namespace Helix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services
            .AddServices()
            .AddDatabase()
            .AddAuthenticationInternal();

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

        services.AddScoped<ISettingsRepository, SettingsRepository>();

        services.AddScoped<IAuditlogRepository, AuditlogRepository>();

        return services;
    }

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICountdownService, CountdownService>();

        // Holds the watched set and the polling loop for the app's lifetime.
        services.AddSingleton<IDriveMonitor, DriveMonitor>();

        services.AddPlatformServices();

        return services;
    }

    /// <summary>
    /// Binds the three abstractions whose implementation is genuinely per-OS: mounting a
    /// share, registering for launch at login, and putting a shortcut on the desktop.
    /// Everything else in this layer is platform-neutral.
    /// </summary>
    private static IServiceCollection AddPlatformServices(this IServiceCollection services)
    {
#if WINDOWS
        // Stateless and cached in viewmodel fields — must survive per-operation scopes.
        services.AddSingleton<INasConnector, WindowsNasConnector>();

        services.AddScoped<IStartupService, WindowsStartupService>();
        services.AddScoped<IDesktopService, WindowsDesktopService>();
#elif MACCATALYST
        services.AddSingleton<INasConnector, MacNasConnector>();

        services.AddScoped<IStartupService, MacStartupService>();
        services.AddScoped<IDesktopService, MacDesktopService>();
#else
        // Fail at composition rather than at the first drive connection: a head added
        // without its platform services would otherwise look fine until it was used.
        throw new PlatformNotSupportedException(
            "Helix has no platform services for this target framework. Add implementations of " +
            $"{nameof(INasConnector)}, {nameof(IStartupService)} and {nameof(IDesktopService)} for it.");
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
