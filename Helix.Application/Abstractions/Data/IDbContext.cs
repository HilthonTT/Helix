using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Helix.Application.Abstractions.Data;

public interface IDbContext
{
    public DbSet<User> Users { get; init; }

    public DbSet<Drive> Drives { get; init; }

    public DbSet<Settings> Settings { get; init; }

    public DbSet<Auditlog> AuditLogs { get; init; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
