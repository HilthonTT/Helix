#if WINDOWS
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Storage;

[SupportedOSPlatform("windows")]
internal sealed class WindowsStorageProbe : StorageProbe
{
    public WindowsStorageProbe(ILogger<WindowsStorageProbe> logger)
        : base(logger)
    {
    }

    protected override string RootPathFor(string letter) => $"{letter.Trim().ToUpperInvariant()}:\\";
}
#endif
