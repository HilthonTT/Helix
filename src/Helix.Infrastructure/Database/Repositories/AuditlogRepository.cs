using Helix.Domain.Auditlogs;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class AuditlogRepository(AppDbContext context) : IAuditlogRepository
{
    public Task<List<Auditlog>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .AuditLogs
            .AsNoTracking()
            .Where(u => u.UserId == userId)
            .OrderByDescending(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Auditlog>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .AuditLogs
            .Where(u => u.UserId == userId)
            .OrderByDescending(a => a.CreatedOnUtc)
            .ToListAsync(cancellationToken);
    }
}
