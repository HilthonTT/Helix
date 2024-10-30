using Helix.Domain.Drives;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Krello.Persistence.Configurations;

internal sealed class DriveConfiguration : IEntityTypeConfiguration<Drive>
{
    public void Configure(EntityTypeBuilder<Drive> builder)
    {
        builder.ToTable(TableNames.Drives);

        builder.HasKey(c => c.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.Name, d.Letter });
    }
}
