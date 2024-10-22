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

namespace Helix.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<UpdateAuditableInterceptor>();
        services.AddSingleton<InsertAuditLogsInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(
                sp.GetRequiredService<UpdateAuditableInterceptor>(),
                sp.GetRequiredService<InsertAuditLogsInterceptor>());
        });

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();

        services.AddScoped<IPasswordHasher, PasswordHasher>();

        services.AddScoped<INasConnector, NasConnector>();

        services.AddScoped<IDbContext, AppDbContext>();

        services.AddScoped<ILoggedInUser, LoggedInUser>();

        return services;
    }
}
