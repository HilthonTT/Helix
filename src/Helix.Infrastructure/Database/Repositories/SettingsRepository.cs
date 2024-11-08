using Helix.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class SettingsRepository(AppDbContext context) : ISettingsRepository
{
    public Task<Settings?> GetByIdAsNoTrackingAsync(Guid settingsId, CancellationToken cancellationToken = default)
    {
        return context
            .Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == settingsId, cancellationToken);
    }

    public Task<Settings?> GetByUserIdAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
    }

    public Task<Settings?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Settings
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
    }

    public void Insert(Settings settings)
    {
        context.Settings.Add(settings);
    }

    public void Remove(Settings settings)
    {
        context.Settings.Remove(settings);
    }
}
