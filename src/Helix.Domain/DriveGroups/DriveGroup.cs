namespace Helix.Domain.DriveGroups;

/// <summary>
/// A named set of drives that are connected and disconnected together — "Home",
/// "Office", "Backup run".
/// </summary>
/// <remarks>
/// Membership is held as drive ids rather than as a navigation to <c>Drive</c>, and the
/// two are deliberately not joined in the database. A group is a convenience the user
/// arranges and rearranges; it does not own the drives in it, several groups may name the
/// same drive, and a drive deleted out from under a group must not take the group with it.
/// Readers resolve the ids against the drives that actually exist, so an id left behind by
/// a deleted drive simply disappears from the group rather than becoming an error.
/// </remarks>
public sealed class DriveGroup : Entity, IAuditable
{
    private readonly List<Guid> _driveIds = [];

    private DriveGroup(Guid id, Guid userId, string name, IEnumerable<Guid> driveIds)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNullOrEmpty(name, nameof(name));

        UserId = userId;
        Name = name;

        _driveIds = Normalize(driveIds);

        DateTime utcNow = DateTime.UtcNow;

        CreatedOnUtc = utcNow;
        ModifiedOnUtc = utcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DriveGroup"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
    private DriveGroup()
    {
        Name = null!;
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    /// <summary>
    /// The drives in the group, in the order they were added, without duplicates.
    /// </summary>
    /// <remarks>
    /// Exposed read-only: membership changes go through <see cref="Update"/> and
    /// <see cref="Remove"/> so the normalizing above cannot be bypassed.
    /// </remarks>
    public IReadOnlyList<Guid> DriveIds => _driveIds;

    public DateTime CreatedOnUtc { get; set; }

    public DateTime? ModifiedOnUtc { get; set; }

    public static DriveGroup Create(Guid userId, string name, IEnumerable<Guid> driveIds) =>
        new(Guid.CreateVersion7(), userId, name, driveIds);

    public void Update(string name, IEnumerable<Guid> driveIds)
    {
        Ensure.NotNullOrEmpty(name, nameof(name));

        Name = name;

        _driveIds.Clear();
        _driveIds.AddRange(Normalize(driveIds));
    }

    /// <summary>
    /// Drops a drive from the group, and reports whether it was in it.
    /// </summary>
    /// <remarks>
    /// Called when a drive is deleted. The group would read correctly without this — a
    /// missing id resolves to nothing — but an install that has been rearranged for a
    /// year should not be carrying a list of ids that no longer name anything.
    /// </remarks>
    public bool Remove(Guid driveId) => _driveIds.Remove(driveId);

    /// <summary>Deduplicates while keeping the order the user chose.</summary>
    private static List<Guid> Normalize(IEnumerable<Guid> driveIds) =>
        [.. driveIds.Where(id => id != Guid.Empty).Distinct()];
}
