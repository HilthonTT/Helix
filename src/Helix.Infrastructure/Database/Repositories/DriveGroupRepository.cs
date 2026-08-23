using Helix.Domain.DriveGroups;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class DriveGroupRepository(AppDbContext context) : IDriveGroupRepository
{
    public Task<List<DriveGroup>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .DriveGroups
            .Where(g => g.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<List<DriveGroup>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .DriveGroups
            .Where(g => g.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public Task<DriveGroup?> GetByIdAsync(Guid driveGroupId, CancellationToken cancellationToken = default)
    {
        return context
            .DriveGroups
            .FirstOrDefaultAsync(g => g.Id == driveGroupId, cancellationToken);
    }

    public Task<DriveGroup?> GetByIdAsNoTrackingAsync(Guid driveGroupId, CancellationToken cancellationToken = default)
    {
        return context
            .DriveGroups
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == driveGroupId, cancellationToken);
    }

    public async Task<bool> IsNameUniqueAsync(
        string name,
        Guid userId,
        Guid? excludingId = null,
        CancellationToken cancellationToken = default)
    {
        // Compared case-insensitively: "Office" and "office" are the same group to anyone
        // reading a row of buttons, whatever the database's collation says.
        //
        // NOCASE rather than LIKE, which this used to use. LIKE reads its right-hand side
        // as a pattern, so a group called "Home_NAS" collided with an existing "HomeXNAS"
        // — the underscore matching any character — and the user was told a name was
        // taken that nothing was using.
        return !await context
            .DriveGroups
            .Where(g => g.UserId == userId)
            .Where(g => excludingId == null || g.Id != excludingId)
            .AnyAsync(g => EF.Functions.Collate(g.Name, "NOCASE") == name, cancellationToken);
    }

    public void Insert(DriveGroup driveGroup)
    {
        context.DriveGroups.Add(driveGroup);
    }

    public void Remove(DriveGroup driveGroup)
    {
        context.DriveGroups.Remove(driveGroup);
    }
}
