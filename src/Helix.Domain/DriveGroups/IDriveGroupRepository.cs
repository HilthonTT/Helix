namespace Helix.Domain.DriveGroups;

public interface IDriveGroupRepository
{
    Task<List<DriveGroup>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Tracked, for the passes that rewrite membership — see <see cref="DriveGroup.Remove"/>.</summary>
    Task<List<DriveGroup>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<DriveGroup?> GetByIdAsync(Guid driveGroupId, CancellationToken cancellationToken = default);

    Task<DriveGroup?> GetByIdAsNoTrackingAsync(Guid driveGroupId, CancellationToken cancellationToken = default);

    /// <param name="excludingId">
    /// The group being renamed, so a group keeping its own name does not collide with
    /// itself.
    /// </param>
    Task<bool> IsNameUniqueAsync(
        string name,
        Guid userId,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default);

    void Insert(DriveGroup driveGroup);

    void Remove(DriveGroup driveGroup);
}
