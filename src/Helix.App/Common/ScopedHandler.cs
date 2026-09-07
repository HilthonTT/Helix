namespace Helix.App.Common;

internal static class ScopedHandler
{
    public static async Task<TResult> HandleAsync<THandler, TResult>(Func<THandler, Task<TResult>> handle)
        where THandler : notnull
    {
        using IServiceScope scope = App.ServiceProvider.CreateScope();

        return await handle(scope.ServiceProvider.GetRequiredService<THandler>());
    }
}
