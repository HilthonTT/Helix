using Helix.Domain.Drives;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class DriveRepository(AppDbContext context) : IDriveRepository
{
    public Task<List<Drive>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Drives
            .Where(d => d.UserId == userId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<List<Drive>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .Drives
            .Where(d => d.UserId != userId)
            .ToListAsync(cancellationToken);
    }

    public Task<Drive?> GetByIdAsNoTrackingAsync(Guid driveId, CancellationToken cancellationToken = default)
    {
        return context
            .Drives
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == driveId, cancellationToken);
    }

    public Task<Drive?> GetByIdAsync(Guid driveId, CancellationToken cancellationToken = default)
    {
        return context
            .Drives
            .FirstOrDefaultAsync(d => d.Id == driveId, cancellationToken);
    }


    public Task<List<string>> GetExistingDriveLettersAsync(
        List<Drive> drives,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        List<Drive> distinctDrives = drives
            .GroupBy(d => d.Letter)
            .Select(g => g.First())
            .ToList();

        return context.Drives
            .Where(d => distinctDrives.Select(dr => dr.Letter).Contains(d.Letter) && d.UserId == userId)
            .Select(d => d.Letter)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsLetterUniqueAsync(string letter, Guid userId, CancellationToken cancellationToken = default)
    {
        string driveLetter = letter.ToUpper();

        return !await context
            .Drives
            .AnyAsync(d => d.Letter == driveLetter && d.UserId == userId, cancellationToken);
    }

    public void Insert(Drive drive)
    {
        context.Drives.Add(drive);
    }

    public void AddRange(IEnumerable<Drive> drives)
    {
        context.Drives.AddRange(drives);
    }

    public void Remove(Drive drive)
    {
        context.Drives.Remove(drive);
    }
}
