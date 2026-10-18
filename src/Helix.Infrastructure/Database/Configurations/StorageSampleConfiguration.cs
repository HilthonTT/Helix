using Helix.Domain.Storage;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helix.Infrastructure.Database.Configurations;

internal sealed class StorageSampleConfiguration : IEntityTypeConfiguration<StorageSample>
{
    public void Configure(EntityTypeBuilder<StorageSample> builder)
    {
        builder.ToTable(TableNames.StorageSamples);

        builder.HasKey(s => s.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.VolumeId).IsRequired();

        builder.Property(s => s.Letters).IsRequired();

        builder.HasIndex(s => new { s.UserId, s.VolumeId, s.Day }).IsUnique();

        builder.HasIndex(s => new { s.UserId, s.Day });
    }
}
