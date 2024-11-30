using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Abstractions.Data;

public interface IDbContext
{
    public DbSet<User> Users { get; init; }

    public DbSet<Drive> Drives { get; init; }

    public DbSet<SettingsModel> Settings { get; init; }

    public DbSet<Auditlog> AuditLogs { get; init; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
