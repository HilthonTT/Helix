namespace Helix.Domain.Drives;

public static class DriveProtocols
{
    public const int WebDavDefaultPort = 443;

    public const int NfsPort = 2049;

    public static readonly IReadOnlyList<int> SmbPorts = [445, 139];

    public static bool IsKnown(DriveProtocol protocol) => Enum.IsDefined(protocol);

    public static bool UsesCredentials(this DriveProtocol protocol) => protocol != DriveProtocol.Nfs;

    public static bool IsValidPort(int? port) => port is null or (> 0 and <= 65_535);

    public static int? PortFor(DriveProtocol protocol, int? port) =>
        protocol == DriveProtocol.WebDav && IsValidPort(port) && port != WebDavDefaultPort ? port : null;

    public static IReadOnlyList<int> ProbePortsFor(DriveProtocol protocol, int? port) => protocol switch
    {
        DriveProtocol.WebDav => [port ?? WebDavDefaultPort],
        DriveProtocol.Nfs => [NfsPort],
        _ => SmbPorts,
    };

    public static string NormalizePath(string path) =>
        string.Join('/', path.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries));
}
