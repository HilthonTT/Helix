using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Helix.Infrastructure.Database.Interceptors;

/// <summary>
/// Stamps <see cref="IAuditable"/> entities on save: <c>CreatedOnUtc</c> when a row is
/// inserted, <c>ModifiedOnUtc</c> whenever it is inserted or changed.
/// </summary>
/// <remarks>
/// Without this the interface was decorative. <see cref="Helix.Domain.Users.User"/> set
/// neither field, so every account was written with <c>CreatedOnUtc = 0001-01-01</c> —
/// the value the index on that column was built over — and no entity ever advanced
/// <c>ModifiedOnUtc</c>, because none of the domain <c>Update</c> methods touched it.
/// Stamping here rather than in each entity keeps the two fields impossible to forget.
/// </remarks>
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
        // Mirror the async path — a synchronous SaveChanges() must not skip the stamp.
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
                    // CreatedOnUtc is deliberately left alone; only the change is dated.
                    entry.Entity.ModifiedOnUtc = utcNow;
                    break;
            }
        }
    }
}
