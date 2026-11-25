#if MACCATALYST
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Storage;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacStorageProbe : StorageProbe
{
    private static readonly string MountRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Helix Drives");

    public MacStorageProbe(ILogger<MacStorageProbe> logger)
        : base(logger)
    {
    }

    protected override string RootPathFor(string letter) =>
        Path.Combine(MountRoot, letter.Trim().ToUpperInvariant());

    protected override (long TotalBytes, long FreeBytes)? ReadCapacity(string rootPath) =>
        IsMountPoint(rootPath) ? base.ReadCapacity(rootPath) : null;

    private static bool IsMountPoint(string rootPath)
    {
        string target = rootPath.TrimEnd(Path.DirectorySeparatorChar);

        try
        {
            return DriveInfo.GetDrives().Any(volume =>
                string.Equals(
                    volume.Name.TrimEnd(Path.DirectorySeparatorChar),
                    target,
                    StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
#endif
