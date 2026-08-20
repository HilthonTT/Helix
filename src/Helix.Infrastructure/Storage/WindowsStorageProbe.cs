#if WINDOWS
using Microsoft.Extensions.Logging;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Storage;

/// <summary>
/// Measures mapped drives on Windows.
/// </summary>
/// <remarks>
/// All this contributes is where a drive letter lives; deciding which mounts are the same
/// storage is <see cref="StorageProbe"/>'s job and is not platform-specific.
///
/// It deliberately does <em>not</em> ask <c>GetVolumeInformation</c> for a volume serial.
/// That looks like the precise answer and is not one for SMB — Samba and most NAS firmware
/// derive the serial per share, so every share of one pool reports a different serial and
/// nothing would ever be recognised as duplicated.
/// </remarks>
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
