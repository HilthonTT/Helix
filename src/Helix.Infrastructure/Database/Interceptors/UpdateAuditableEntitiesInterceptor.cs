using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Helix.Infrastructure.Database.Interceptors;

internal sealed class UpdateAuditableEntitiesInterceptor(IDateTimeProvider dateTimeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            Stamp(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            Stamp(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Stamp(DbContext context)
    {
        DateTime utcNow = dateTimeProvider.UtcNow;

        foreach (EntityEntry<IAuditable> entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedOnUtc = utcNow;
                    entry.Entity.ModifiedOnUtc = utcNow;
                    break;

                case EntityState.Modified:
                    entry.Entity.ModifiedOnUtc = utcNow;
                    break;
            }
        }
    }
}
