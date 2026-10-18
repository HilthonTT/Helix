using Helix.Domain.Storage;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class StorageSampleRepository(AppDbContext context) : IStorageSampleRepository
{
    public Task<List<StorageSample>> GetSinceAsync(
        Guid userId,
        DateOnly since,
        CancellationToken cancellationToken = default)
    {
        return context
            .StorageSamples
            .Where(s => s.UserId == userId && s.Day >= since)
            .OrderBy(s => s.Day)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<List<StorageSample>> GetForDayAsync(
        Guid userId,
        DateOnly day,
        CancellationToken cancellationToken = default)
    {
        return context
            .StorageSamples
            .Where(s => s.UserId == userId && s.Day == day)
            .ToListAsync(cancellationToken);
    }

    public Task<int> RemoveOlderThanAsync(Guid userId, DateOnly day, CancellationToken cancellationToken = default)
    {
        return context
            .StorageSamples
            .Where(s => s.UserId == userId && s.Day < day)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public void Insert(StorageSample sample)
    {
        context.StorageSamples.Add(sample);
    }
}
