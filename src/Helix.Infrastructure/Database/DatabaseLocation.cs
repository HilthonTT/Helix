using Helix.Infrastructure.Database.Constants;

namespace Helix.Infrastructure.Database;

/// <summary>
/// Where the database file lives, so the context that opens it and the startup that
/// reports on it cannot disagree.
/// </summary>
/// <remarks>
/// The directory is MAUI's per-user app data folder, which on an unpackaged Windows
/// build is <c>%LOCALAPPDATA%\&lt;publisher&gt;\&lt;package&gt;\Data</c>, and both of
/// those names come out of the <b>build</b>: the AppInfo metadata the resizetizer stamps
/// from a <c>Package.appxmanifest</c> when the project has one, and the assembly's company
/// and title attributes when it does not. The release workflow builds from a checkout
/// with no manifest — it is ignored — so every published build resolves to
/// <c>Helix.App\Helix.App\Data</c>. A machine that builds with a manifest in place
/// resolves to whatever that manifest says, and a copy of the app produced that way keeps
/// its data in a folder the published builds never look in. Updating from one to the
/// other therefore comes up with an empty database and no account. The logging in
/// <see cref="DatabaseInitializer"/> exists so that, when it happens, the diagnostics zip
/// says where the app looked.
/// </remarks>
internal static class DatabaseLocation
{
    /// <summary>The directory the database and the logs are kept in.</summary>
    public static string Directory => FileSystem.AppDataDirectory;

    /// <summary>The full path of the database file, whether or not it exists yet.</summary>
    public static string Path => System.IO.Path.Combine(Directory, DatabaseConfiguration.DatabaseName);
}
