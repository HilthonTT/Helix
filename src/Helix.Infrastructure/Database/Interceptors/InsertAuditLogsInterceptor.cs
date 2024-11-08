using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
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
        List<Auditlog> auditLogs = GetDrivesAuditLogs(context).ToList();

        context.Set<Auditlog>().AddRange(auditLogs);
    }

    private static IEnumerable<Auditlog> GetDrivesAuditLogs(DbContext context)
    {
        DateTime utcNow = DateTime.UtcNow;


        return context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.Entity is Drive &&
                            (entry.State == EntityState.Added || 
                             entry.State == EntityState.Modified ||
                             entry.State == EntityState.Deleted))
            .Select(entry =>
            {
                Drive drive = (Drive)entry.Entity;

                string action = entry.State switch
                {
                    EntityState.Added => "created",
                    EntityState.Modified => "updated",
                    EntityState.Deleted => "deleted",
                    _ => "modified"
                };

                return Auditlog.Create(
                    drive.UserId,
                    $"Drive '{drive.Name}' (ID: {drive.Id}) has been {action}."
            );
        });
    }
}
