using Helix.Domain.DriveGroups;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helix.Infrastructure.Database.Configurations;

internal sealed class DriveGroupConfiguration : IEntityTypeConfiguration<DriveGroup>
{
    public void Configure(EntityTypeBuilder<DriveGroup> builder)
    {
        builder.ToTable(TableNames.DriveGroups);

        builder.HasKey(g => g.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(g => g.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Membership is a primitive collection, stored as a JSON array in one column,
        // rather than a join table to Drives. A group does not own its drives — several
        // may name the same one, and a deleted drive must not take the group with it —
        // so the referential integrity a join table would enforce is the wrong shape
        // here. Readers resolve the ids against the drives that exist.
        builder.PrimitiveCollection(g => g.DriveIds)
            .HasField("_driveIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(g => new { g.UserId, g.Name }).IsUnique();

        builder.HasIndex(g => g.CreatedOnUtc);
    }
}
