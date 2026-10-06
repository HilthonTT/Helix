using Helix.Domain.DriveGroups;
using Helix.Domain.Schedules;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helix.Infrastructure.Database.Configurations;

internal sealed class ScheduleConfiguration : IEntityTypeConfiguration<Schedule>
{
    public void Configure(EntityTypeBuilder<Schedule> builder)
    {
        builder.ToTable(TableNames.Schedules);

        builder.HasKey(s => s.Id);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<DriveGroup>()
            .WithMany()
            .HasForeignKey(s => s.DriveGroupId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(s => s.Action).HasConversion<int>();

        builder.Property(s => s.Days).HasConversion<int>();

        builder.Ignore(s => s.Disconnects);

        builder.HasIndex(s => s.UserId);
    }
}
