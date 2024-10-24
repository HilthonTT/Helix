using Microsoft.EntityFrameworkCore;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Core.Extensions;

public static class SettingsDbSetExtensions
{
    public static Task<SettingsModel?> GetByIdAsNoTrackingAsync(
        this DbSet<SettingsModel> settings,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public static Task<SettingsModel?> GetByUserIdAsNoTrackingAsync(
        this DbSet<SettingsModel> settings,
        Guid userId, 
        CancellationToken cancellationToken = default)
    {
        return settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken);
    }
}
