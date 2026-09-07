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
}
#endif
