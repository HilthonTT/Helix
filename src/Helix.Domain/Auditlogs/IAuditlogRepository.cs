namespace Helix.Domain.Auditlogs;

public interface IAuditlogRepository
{
    Task<List<Auditlog>> GetAsNoTrackingAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<List<Auditlog>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an entry that no entity change would produce on its own. Connecting a
    /// share touches Windows rather than the database, so drops and recoveries are
    /// invisible to the save interceptor and have to be written explicitly.
    /// </summary>
    void Insert(Auditlog auditlog);
}
