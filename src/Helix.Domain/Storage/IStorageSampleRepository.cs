namespace Helix.Domain.Storage;

public interface IStorageSampleRepository
{
    Task<List<StorageSample>> GetSinceAsync(Guid userId, DateOnly since, CancellationToken cancellationToken = default);

    Task<List<StorageSample>> GetForDayAsync(Guid userId, DateOnly day, CancellationToken cancellationToken = default);

    Task<int> RemoveOlderThanAsync(Guid userId, DateOnly day, CancellationToken cancellationToken = default);

    void Insert(StorageSample sample);
}
