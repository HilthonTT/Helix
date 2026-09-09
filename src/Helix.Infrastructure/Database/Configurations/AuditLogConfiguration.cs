using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using Helix.Infrastructure.Database.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Helix.Infrastructure.Database.Configurations;

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

        // The history is only ever read one user's newest-first page at a time, so the
        // index carries the user as well: without it every page is a scan and a sort.
        builder.HasIndex(a => new { a.UserId, a.CreatedOnUtc });
    }
}
