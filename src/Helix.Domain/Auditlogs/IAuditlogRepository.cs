namespace Helix.Domain.Auditlogs;

public interface IAuditlogRepository
{
    Task<List<Auditlog>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<Auditlog>> GetAsync(Guid userId, CancellationToken cancellationToken = default);
}
