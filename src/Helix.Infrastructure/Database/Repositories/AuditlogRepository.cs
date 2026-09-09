using Helix.Domain.Auditlogs;
using Microsoft.EntityFrameworkCore;

namespace Helix.Infrastructure.Database.Repositories;

internal sealed class AuditlogRepository(AppDbContext context) : IAuditlogRepository
{
    public Task<List<Auditlog>> GetPageAsNoTrackingAsync(
        Guid userId,
        int skip,
        int take,
        bool oldestFirst,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Auditlog> query = context
            .AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == userId);

        // The id breaks the tie so a page boundary cannot fall inside a group of rows
        // written in the same tick and hand the same row back twice.
        query = oldestFirst
            ? query.OrderBy(a => a.CreatedOnUtc).ThenBy(a => a.Id)
            : query.OrderByDescending(a => a.CreatedOnUtc).ThenByDescending(a => a.Id);

        return query
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context
            .AuditLogs
            .AsNoTracking()
            .CountAsync(a => a.UserId == userId, cancellationToken);
    }

    public void Insert(Auditlog auditlog)
    {
        context.AuditLogs.Add(auditlog);
    }

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
