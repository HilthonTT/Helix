using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel;

namespace Helix.Persistence.Interceptors;

internal sealed class InsertAuditLogsInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            InsertAuditLogs(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void InsertAuditLogs(DbContext context)
    {
        List<Auditlog> auditLogs = GetCredentialAuditLogs(context)
            .Concat(GetUserAuditLogs(context))
            .ToList();

        context.Set<Auditlog>().AddRange(auditLogs);
    }

    private static IEnumerable<Auditlog> GetCredentialAuditLogs(DbContext context)
    {
        // Filter credentials that are added or modified
        return context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.Entity is Drive &&
                            (entry.State == EntityState.Added || entry.State == EntityState.Modified))
            .Select(entry =>
            {
                var drive = (Drive)entry.Entity;
                var action = entry.State == EntityState.Added ? "created" : "updated";
                return Auditlog.Create(
                    userId: drive.UserId,
                    message: $"Drive for user {drive.UserId} was {action}.");
            });
    }

    private static IEnumerable<Auditlog> GetUserAuditLogs(DbContext context)
    {
        // Filter user updates only (no creation or deletion)
        return context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.Entity is User && entry.State == EntityState.Modified)
            .Select(entry =>
            {
                var user = (User)entry.Entity;
                return Auditlog.Create(
                    userId: user.Id,
                    message: $"User {user.Id} was updated.");
            });
    }
}
