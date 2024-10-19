using Helix.Infrastructure.Database.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;

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

        optionsBuilder.UseSqlite(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
