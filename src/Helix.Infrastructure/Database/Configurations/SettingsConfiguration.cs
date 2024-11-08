using Helix.Domain.Settings;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Krello.Persistence.Configurations;

internal sealed class SettingsConfiguration : IEntityTypeConfiguration<Settings>
{
    public void Configure(EntityTypeBuilder<Settings> builder)
    {
        builder.ToTable(TableNames.Settings);

        builder.HasKey(s => s.Id);

        builder.HasOne<User>()
           .WithOne()
           .HasForeignKey<Settings>(s => s.UserId)
           .IsRequired()
           .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.UserId).IsUnique();
    }
}
