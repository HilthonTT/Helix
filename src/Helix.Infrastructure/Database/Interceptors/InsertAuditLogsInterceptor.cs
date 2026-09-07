using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Helix.Infrastructure.Database.Interceptors;

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

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            InsertAuditLogs(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private static void InsertAuditLogs(DbContext context)
    {
        List<Auditlog> auditLogs = GetDrivesAuditLogs(context).ToList();

        context.Set<Auditlog>().AddRange(auditLogs);
    }

    private static IEnumerable<Auditlog> GetDrivesAuditLogs(DbContext context)
    {
        return context.ChangeTracker
            .Entries<Entity>()
            .Where(entry => entry.Entity is Drive &&
                            (entry.State == EntityState.Added ||
                             entry.State == EntityState.Modified ||
                             entry.State == EntityState.Deleted))
            .Where(IsWorthRecording)
            .Select(entry =>
            {
                var drive = (Drive)entry.Entity;

                AuditAction action = entry.State switch
                {
                    EntityState.Added => AuditAction.DriveCreated,
                    EntityState.Deleted => AuditAction.DriveDeleted,
                    _ => AuditAction.DriveUpdated,
                };

                return Auditlog.ForDrive(drive.UserId, action, drive.Id, drive.Name, drive.Letter);
            });
    }

    private static bool IsWorthRecording(EntityEntry<Entity> entry)
    {
        if (entry.State != EntityState.Modified)
        {
            return true;
        }

        return entry.Properties.Any(property =>
            property.IsModified &&
            property.Metadata.Name is not (nameof(Drive.LastConnectedOnUtc) or nameof(IAuditable.ModifiedOnUtc)));
    }
}
