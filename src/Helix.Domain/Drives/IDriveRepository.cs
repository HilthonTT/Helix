namespace Helix.Domain.Drives;

public interface IDriveRepository
{
    Task<bool> IsLetterUniqueAsync(string letter, Guid userId, CancellationToken cancellationToken = default);

    Task<Drive?> GetByIdAsync(Guid driveId, CancellationToken cancellationToken = default);

    Task<Drive?> GetByIdAsNoTrackingAsync(Guid driveId, CancellationToken cancellationToken = default);

    Task<List<Drive>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<Drive>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<string>> GetExistingDriveLettersAsync(List<Drive> drives, CancellationToken cancellationToken = default);

    void Insert(Drive drive);

    void AddRange(IEnumerable<Drive> drives);

    void Remove(Drive drive);
}
