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

    public void Insert(Auditlog auditlog)
    {
        context.AuditLogs.Add(auditlog);
    }

    /// <remarks>
    /// A single DELETE rather than loading every expired row into the change tracker to
    /// remove it again. It also writes immediately, outside the unit of work — which is
    /// what is wanted here: pruning is housekeeping, not part of whatever else the
    /// surrounding scope was doing.
    /// </remarks>
    public Task<int> DeleteOlderThanAsync(
        Guid userId,
        DateTime cutoffUtc,
        CancellationToken cancellationToken = default)
    {
        return context
            .AuditLogs
            .Where(a => a.UserId == userId && a.CreatedOnUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
