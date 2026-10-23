using Helix.Infrastructure.Database.Constants;

namespace Helix.Infrastructure.Database;

internal static class DatabaseLocation
{
    public static string Directory => FileSystem.AppDataDirectory;

    public static string Path => System.IO.Path.Combine(Directory, DatabaseConfiguration.DatabaseName);

    public static string DataSource => DataSourceFor(Path);

    public static string DataSourceFor(string path)
    {
        string escaped = path
            .Replace('\\', '/')
            .Replace("%", "%25")
            .Replace("?", "%3F")
            .Replace("#", "%23");

        string root = escaped.StartsWith('/') ? "file://" : "file:///";

        return $"{root}{escaped}?{DatabaseConfiguration.CipherParameters}";
    }
}
