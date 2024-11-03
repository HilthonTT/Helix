using Helix.Application.Abstractions.Data;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using Helix.Infrastructure.Cryptography;
using Helix.Infrastructure.Database.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Helix.Infrastructure.Database;

public sealed class AppDbContext : DbContext, IDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; init; }

    public DbSet<Drive> Drives { get; init; }

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

        string dbPath = Path.Combine(FileSystem.AppDataDirectory, DatabaseConfiguration.DatabaseName);

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = PasswordGenerator.GetOrCreatePassword(),
        }.ToString();

        var connection = new SqliteConnection(connectionString);

        optionsBuilder
            .UseSqlite(connection)
            .ReplaceService<IRelationalCommandBuilderFactory, CustomRelationalCommandBuilderFactory>();
    }
}
