namespace Helix.Domain.Auditlogs;

public interface IAuditlogRepository
{
    Task<List<Auditlog>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<Auditlog>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    void Insert(Auditlog auditlog);

    Task<int> DeleteOlderThanAsync(Guid userId, DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
