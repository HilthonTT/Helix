using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Helix.Infrastructure.Database;

/// <summary>
/// Brings the database up to the running version's schema at startup, and writes down
/// enough about what it found to explain an empty one afterwards.
/// </summary>
/// <remarks>
/// A user who updated and was greeted by the sign-in page with no account had nothing
/// to send that said where the app had looked, whether a database had been there, or
/// whether the schema had been touched. All three are Information-level now, in the
/// file the settings page exports.
///
/// Before a migration runs against an existing database, a copy is put beside it as
/// <c>helix.db.bak</c>. A migration that goes wrong halfway is the one failure that turns
/// an update into a loss of every drive, and the copy — the same encrypted bytes, under
/// the same key — is what makes that recoverable by renaming a file. It is overwritten by
/// the next migration, so only the version before the running one is ever kept.
/// </remarks>
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

            File.Copy(path, backupPath, overwrite: true);

            logger.LogInformation(
                "Applying {Count} migration(s); the database before them is kept at {Backup}.",
                pending.Length,
                Path.GetFileName(backupPath));
        }
        catch (Exception ex)
        {
            // The copy is a safety net, not a precondition: an update that cannot make one
            // still has to bring the schema up, or the app does not start at all.
            logger.LogWarning(ex, "Could not back the database up before migrating it.");
        }
    }
}
