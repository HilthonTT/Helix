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

        builder.PrimitiveCollection(g => g.DriveIds)
            .HasField("_driveIds")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(g => new { g.UserId, g.Name }).IsUnique();

        builder.HasIndex(g => g.CreatedOnUtc);
    }
}
