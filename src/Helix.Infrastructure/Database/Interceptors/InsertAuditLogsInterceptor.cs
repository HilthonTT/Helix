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
        // Mirror the async path — a synchronous SaveChanges() call must not
        // silently skip audit logging.
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

    /// <summary>
    /// Turns the drive changes in this save into audit entries.
    /// </summary>
    /// <remarks>
    /// The entry records the action and the drive, not a sentence about them. Composing
    /// English here was what made the audit page untranslatable in an app that ships in
    /// six languages — see <see cref="AuditAction"/>.
    /// </remarks>
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

    /// <summary>
    /// Whether a change is one the user would recognise as a change to the drive.
    /// </summary>
    /// <remarks>
    /// Connecting a drive now stamps <c>LastConnectedOnUtc</c>, and the auditable
    /// interceptor stamps <c>ModifiedOnUtc</c> alongside it. Both are bookkeeping. Left
    /// unfiltered, every single connect would file a "the drive was changed" entry and
    /// bury the events that actually matter.
    /// </remarks>
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
