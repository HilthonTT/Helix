using Helix.Application.Abstractions.Data;
using Helix.Domain.Auditlogs;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using Helix.Infrastructure.Cryptography;
using Helix.Infrastructure.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Helix.Infrastructure.Database;

public sealed class AppDbContext : DbContext, IUnitOfWork, IDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; init; }

    public DbSet<Drive> Drives { get; init; }

    public DbSet<DriveGroup> DriveGroups { get; init; }

    public DbSet<Settings> Settings { get; init; }

    public DbSet<Auditlog> AuditLogs { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (optionsBuilder.IsConfigured)
        {
            return;
        }

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseLocation.Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = PasswordGenerator.GetOrCreatePassword(),
        }.ToString();

        optionsBuilder
            .UseSqlite(connectionString)
            .ReplaceService<IRelationalCommandBuilderFactory, CustomRelationalCommandBuilderFactory>();
    }

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }
}
