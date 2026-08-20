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

    /// <summary>
    /// Deletes this user's entries older than <paramref name="cutoffUtc"/> and returns how
    /// many went. Scoped to the user like every other query here — one account trimming
    /// its own history must never touch another's.
    /// </summary>
    Task<int> DeleteOlderThanAsync(Guid userId, DateTime cutoffUtc, CancellationToken cancellationToken = default);
}
