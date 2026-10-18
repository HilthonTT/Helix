namespace Helix.Domain.Storage;

public sealed class StorageSample : Entity
{
    private StorageSample(
        Guid id,
        Guid userId,
        string volumeId,
        DateOnly day,
        long totalBytes,
        long freeBytes,
        string letters,
        DateTime recordedOnUtc)
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNullOrEmpty(volumeId, nameof(volumeId));

        UserId = userId;
        VolumeId = volumeId;
        Day = day;
        TotalBytes = totalBytes;
        FreeBytes = freeBytes;
        Letters = letters;
        RecordedOnUtc = recordedOnUtc;
    }

    private StorageSample()
    {
        VolumeId = null!;
        Letters = null!;
    }

    public Guid UserId { get; private set; }

    public string VolumeId { get; private set; }

    public DateOnly Day { get; private set; }

    public long TotalBytes { get; private set; }

    public long FreeBytes { get; private set; }

    public string Letters { get; private set; }

    public DateTime RecordedOnUtc { get; private set; }

    public static StorageSample Create(
        Guid userId,
        string volumeId,
        DateOnly day,
        long totalBytes,
        long freeBytes,
        string letters,
        DateTime recordedOnUtc) =>
        new(Guid.CreateVersion7(), userId, volumeId, day, totalBytes, freeBytes, letters, recordedOnUtc);

    public void Update(long totalBytes, long freeBytes, string letters, DateTime recordedOnUtc)
    {
        TotalBytes = totalBytes;
        FreeBytes = freeBytes;
        Letters = letters;
        RecordedOnUtc = recordedOnUtc;
    }
}
