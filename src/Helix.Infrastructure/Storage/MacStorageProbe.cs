#if MACCATALYST
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Storage;

/// <summary>
/// Measures the mounts under the Helix mount root.
/// </summary>
/// <remarks>
/// As on Windows, all this contributes is where a letter lives — the deduplication rule
/// in <see cref="StorageProbe"/> is the same on both heads.
/// </remarks>
[SupportedOSPlatform("maccatalyst")]
internal sealed class MacStorageProbe : StorageProbe
{
    /// <summary>
    /// Mirrors <c>MacNasConnector.MountRoot</c>: a letter is a directory under it.
    /// </summary>
    private static readonly string MountRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Helix Drives");

    public MacStorageProbe(ILogger<MacStorageProbe> logger)
        : base(logger)
    {
    }

    protected override string RootPathFor(string letter) =>
        Path.Combine(MountRoot, letter.Trim().ToUpperInvariant());
}
#endif
