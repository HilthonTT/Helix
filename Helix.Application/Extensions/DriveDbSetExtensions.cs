using Helix.Domain.Drives;
using Microsoft.EntityFrameworkCore;

namespace Helix.Application.Extensions;

public static class DriveDbSetExtensions
{
    public static Task<bool> ExistsWithLetterAsync(
        this DbSet<Drive> drives, 
        string letter, 
        CancellationToken cancellationToken = default)
    {
        string driveLetter = letter.ToUpper();

        return drives.AnyAsync(d => d.Letter == driveLetter, cancellationToken);
    }

    public static Task<Drive?> GetByIdAsync(
        this DbSet<Drive> drives, 
        Guid driveId,
        CancellationToken cancellationToken = default)
    {
        return drives.FirstOrDefaultAsync(d => d.Id == driveId, cancellationToken);
    }

    public static Task<Drive?> GetByIdAsNoTrackingAsync(
        this DbSet<Drive> drives,
        Guid driveId,
        CancellationToken cancellationToken = default)
    {
        return drives
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driveId, cancellationToken);
    }
}