namespace Helix.Domain.Auditlogs;

public interface IAuditlogRepository
{
    Task<List<Auditlog>> GetPageAsNoTrackingAsync(
        Guid userId,
        int skip,
        int take,
        bool oldestFirst,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid userId, CancellationToken cancellationToken = default);

    void Insert(Auditlog auditlog);

    Task<int> DeleteOlderThanAsync(Guid userId, DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
