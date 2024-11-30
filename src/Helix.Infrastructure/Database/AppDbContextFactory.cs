using Helix.Infrastructure.Database.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Database;

internal sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabaseConfiguration.TemporaryDatabaseName,
            Password = DatabaseConfiguration.DesignTimePassword
        }.ToString();

        optionsBuilder
            .UseSqlite(connectionString)
            .EnableSensitiveDataLogging()
            .LogTo(Console.WriteLine, LogLevel.Information);

        return new AppDbContext(optionsBuilder.Options);
    }
}
