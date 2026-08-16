namespace Helix.App.Common;

/// <summary>
/// Runs an Application-layer handler inside its own DI scope so every operation
/// gets a fresh <c>AppDbContext</c>. Resolving scoped handlers from the root
/// provider (and caching them in viewmodel fields) made them de-facto singletons
/// sharing one DbContext, so overlapping async operations raced on it
/// ("A second operation was started on this context instance...").
/// </summary>
internal static class ScopedHandler
{
    public static async Task<TResult> HandleAsync<THandler, TResult>(Func<THandler, Task<TResult>> handle)
        where THandler : notnull
    {
        using IServiceScope scope = App.ServiceProvider.CreateScope();

        return await handle(scope.ServiceProvider.GetRequiredService<THandler>());
    }
}
