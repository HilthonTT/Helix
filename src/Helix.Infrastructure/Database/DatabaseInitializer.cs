using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Database;

public static class DatabaseInitializer
{
    private const string BackupSuffix = ".bak";

    public static void Initialize(AppDbContext context, ILogger logger)
    {
        string path = DatabaseLocation.Path;
        bool existed = File.Exists(path);

        logger.LogInformation(
            "Database at {Directory}: {State}.",
            DatabaseLocation.Directory,
            existed ? "found" : "not found, a new one will be created");

        if (existed)
        {
            BackUpBeforeMigrating(context, path, logger);
        }

        context.Database.Migrate();
    }

    private static void BackUpBeforeMigrating(AppDbContext context, string path, ILogger logger)
    {
        try
        {
            string[] pending = [.. context.Database.GetPendingMigrations()];
            if (pending.Length == 0)
            {
                return;
            }

            string backupPath = path + BackupSuffix;

            context.Database.ExecuteSqlRaw("PRAGMA wal_checkpoint(TRUNCATE);");

            File.Copy(path, backupPath, overwrite: true);

            logger.LogInformation(
                "Applying {Count} migration(s); the database before them is kept at {Backup}.",
                pending.Length,
                Path.GetFileName(backupPath));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not back the database up before migrating it.");
        }
    }
}
