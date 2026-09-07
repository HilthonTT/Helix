namespace Helix.Domain.DriveGroups;

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

    private DriveGroup()
    {
        Name = null!;
    }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

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

    public bool Remove(Guid driveId) => _driveIds.Remove(driveId);

    private static List<Guid> Normalize(IEnumerable<Guid> driveIds) =>
        [.. driveIds.Where(id => id != Guid.Empty).Distinct()];
}
