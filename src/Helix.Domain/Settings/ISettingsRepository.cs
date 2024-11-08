namespace Helix.Domain.Settings;

public interface ISettingsRepository
{
    Task<Settings?> GetByIdAsNoTrackingAsync(Guid settingsId, CancellationToken cancellationToken = default);

    Task<Settings?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Settings?> GetByUserIdAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    void Insert(Settings settings);

    void Remove(Settings settings);
}
