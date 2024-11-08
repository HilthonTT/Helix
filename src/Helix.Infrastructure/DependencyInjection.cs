using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Connector;
using Helix.Infrastructure.Authentication;
using Helix.Infrastructure.Cryptography;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Connector;
using Helix.Persistence.Interceptors;
using SharedKernel;
using Helix.Infrastructure.Time;
using Helix.Application.Abstractions.Caching;
using Helix.Infrastructure.Caching;
using Helix.Application.Abstractions.Time;
using Helix.Application.Abstractions.Startup;
using Helix.Infrastructure.Startup;
using Helix.Application.Abstractions.Desktop;
using Helix.Infrastructure.Desktop;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Repositories;
using Helix.Domain.Drives;
using Helix.Domain.Settings;

namespace Helix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services
            .AddServices()
            .AddDatabase()
            .AddCaching()
            .AddAuthenticationInternal();

        return services;
    }

    private static IServiceCollection AddDatabase(this IServiceCollection services)
    {
        services.AddSingleton<InsertAuditLogsInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<InsertAuditLogsInterceptor>());
        });

        services.AddScoped<IDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();

        services.AddScoped<IDriveRepository, DriveRepository>();

        services.AddScoped<ISettingsRepository, SettingsRepository>();

        return services;
    }

    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
        services.AddSingleton<ICountdownService, CountdownService>();

        services.AddScoped<INasConnector, NasConnector>();
        services.AddScoped<IStartupService, StartupService>();
        services.AddScoped<IDesktopService, DesktopService>();

        return services;
    }

    private static IServiceCollection AddAuthenticationInternal(this IServiceCollection services)
    {
        services.AddScoped<IPasswordHasher, PasswordHasher>();

        services.AddScoped<ILoggedInUser, LoggedInUser>();

        return services;
    }

    private static IServiceCollection AddCaching(this IServiceCollection services)
    {
        services.AddDistributedMemoryCache();

        services.AddSingleton<ICacheService, CacheService>();

        return services;
    }
}
