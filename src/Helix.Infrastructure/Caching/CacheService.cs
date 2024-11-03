using Helix.Application.Abstractions.Caching;
using Microsoft.Extensions.Caching.Distributed;
using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Helix.Infrastructure.Caching;

internal sealed class CacheService(IDistributedCache cache) : ICacheService
{
    private static readonly ConcurrentDictionary<string, bool> KeyTracker = new();

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        byte[]? bytes = await cache.GetAsync(key, cancellationToken);

        return bytes is null ? default : Deserialize<T>(bytes);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default)
    {
        byte[] bytes = Serialize(value);

        KeyTracker.TryAdd(key, true);

        return cache.SetAsync(key, bytes, CacheOptions.Create(expiration), cancellationToken);
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        KeyTracker.TryRemove(key, out _);

        return cache.RemoveAsync(key, cancellationToken);
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        IEnumerable<Task> tasks = KeyTracker.Keys
            .Where(key => key.StartsWith(prefix))
            .Select(key => RemoveAsync(key, cancellationToken));

        return Task.WhenAll(tasks);
    }

    private static T Deserialize<T>(byte[] bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes)!;
    }

    private static byte[] Serialize<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        JsonSerializer.Serialize(writer, value);

        return buffer.WrittenSpan.ToArray();
    }
}
