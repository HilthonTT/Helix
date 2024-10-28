using Helix.Application.Abstractions.Handlers;
using System.Reflection;

namespace Helix.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddHandlers();

        return services;
    }

    /// <summary>
    /// Scans the current assembly for all non-abstract classes that implement the <see cref="IHandler"/> interface 
    /// and registers them as scoped services in the specified <paramref name="services"/> collection.
    /// </summary>
    /// <param name="services">The service collection to which the handler services will be added.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance, enabling method chaining.</returns>
    private static IServiceCollection AddHandlers(this IServiceCollection services)
    {
        Assembly assembly = typeof(DependencyInjection).Assembly;

        TypeInfo[] handlerTypes = assembly
            .DefinedTypes
            .Where(type => type is { IsAbstract: false, IsInterface: false } &&
                           type.IsAssignableTo(typeof(IHandler)))
            .ToArray();

        foreach (TypeInfo handlerType in handlerTypes)
        {
            services.AddScoped(handlerType);
        }

        return services;
    }
}
