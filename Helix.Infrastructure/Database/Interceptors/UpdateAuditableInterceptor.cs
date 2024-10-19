using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SharedKernel;

namespace Helix.Persistence.Interceptors;

internal sealed class UpdateAuditableInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            UpdateAuditableEntities(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void UpdateAuditableEntities(DbContext context)
    {
        DateTime utcNow = DateTime.UtcNow;

        List<EntityEntry<IAuditable>> entities = context.ChangeTracker.Entries<IAuditable>().ToList();

        foreach (EntityEntry<IAuditable> entry in entities)
        {
            if (entry.State == EntityState.Added)
            {
                SetCurrentPropertyValue(entry, nameof(IAuditable.CreatedOnUtc), utcNow);
            }

            if (entry.State == EntityState.Modified)
            {
                SetCurrentPropertyValue(entry, nameof(IAuditable.ModifiedOnUtc), utcNow);
            }
        }

        static void SetCurrentPropertyValue(EntityEntry entry, string propertyName, DateTime utcNow)
        {
            entry.Property(propertyName).CurrentValue = utcNow;
        }
    }
}
