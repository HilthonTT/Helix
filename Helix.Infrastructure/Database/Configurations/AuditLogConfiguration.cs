using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helix.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<Auditlog>
{
    public void Configure(EntityTypeBuilder<Auditlog> builder)
    {
        builder.ToTable(TableNames.AuditLogs);

        builder.HasKey(a => a.Id);

        builder.HasOne<User>()
           .WithMany()
           .HasForeignKey(a => a.UserId)
           .IsRequired()
           .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.CreatedOnUtc);
    }
}
