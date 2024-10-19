using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Infrastructure.Cryptography;
using Helix.Infrastructure.Database;
using Helix.Persistence.Interceptors;

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

        services.AddScoped<IPasswordHasher, PasswordHasher>();

        services.AddScoped<IDbContext, AppDbContext>();

        return services;
    }
}
