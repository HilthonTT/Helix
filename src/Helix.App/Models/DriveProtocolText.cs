using Helix.App.Resources.Languages;
using Helix.Domain.Drives;
using System.Globalization;

namespace Helix.App.Models;

internal static class DriveProtocolText
{
    public static IReadOnlyList<string> Names =>
        [AppResources.ProtocolSmb, AppResources.ProtocolWebDav, AppResources.ProtocolNfs];

    public static DriveProtocol FromIndex(int index) =>
        Enum.IsDefined((DriveProtocol)index) ? (DriveProtocol)index : DriveProtocol.Smb;

    public static string PathHintFor(DriveProtocol protocol) => protocol switch
    {
        DriveProtocol.WebDav => AppResources.ProtocolWebDavHint,
        DriveProtocol.Nfs => AppResources.ProtocolNfsHint,
        _ => string.Empty,
    };

    public static string ShortName(DriveProtocol protocol) => protocol switch
    {
        DriveProtocol.WebDav => "WebDAV",
        DriveProtocol.Nfs => "NFS",
        _ => "SMB",
    };

    public static int? ParsePort(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port) ? port : 0;
    }
}
