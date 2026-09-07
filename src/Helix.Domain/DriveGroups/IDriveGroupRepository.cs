namespace Helix.Domain.DriveGroups;

public interface IDriveGroupRepository
{
    Task<List<DriveGroup>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<DriveGroup>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<DriveGroup?> GetByIdAsync(Guid driveGroupId, CancellationToken cancellationToken = default);

    Task<DriveGroup?> GetByIdAsNoTrackingAsync(Guid driveGroupId, CancellationToken cancellationToken = default);

    Task<bool> IsNameUniqueAsync(
        string name,
        Guid userId,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default);

    void Insert(DriveGroup driveGroup);

    void Remove(DriveGroup driveGroup);
}
