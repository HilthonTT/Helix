using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

/// <summary>
/// Maps network drives via the Win32 <c>mpr.dll</c> WNet APIs. Replaces the
/// previous <c>net.exe</c> shell-out which exposed NAS credentials in the
/// process command line and risked argument-injection through user-supplied
/// passwords.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNasConnector : INasConnector
{
    private const int ConnectionTimeoutMilliseconds = 5_000;

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Connect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
            failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
            cancellationToken);

    public Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Disconnect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToDisconnect("Disconnection timed out.")),
            failure: message => Result.Failure(DriveErrors.FailedToDisconnect(message)),
            cancellationToken);

    public Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Test(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
            failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
            cancellationToken);

    public bool IsConnected(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter))
        {
            return false;
        }

        string prefix = $"{letter.ToUpperInvariant()}:\\";
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the set of drive letters that are currently mounted (uppercased,
    /// without colon or trailing slash). Callers that need to check connection
    /// status for many drives should use this once instead of calling
    /// <see cref="IsConnected"/> in a loop.
    /// </summary>
    public HashSet<string> GetConnectedLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            // drive.Name is typically "C:\" — take the first character.
            if (drive.Name.Length > 0 && char.IsLetter(drive.Name[0]))
            {
                letters.Add(drive.Name[0].ToString().ToUpperInvariant());
            }
        }

        return letters;
    }

    private static Result Connect(Drive drive)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpLocalName = $"{drive.Letter.ToUpperInvariant()}:",
            lpRemoteName = RemoteNameFor(drive),
            lpProvider = null,
        };

        // CONNECT_UPDATE_PROFILE writes the mapping into the user profile, so Explorer
        // restores it at sign-in without Helix running. CONNECT_TEMPORARY is the opposite
        // and stays the default: the mapping lives exactly as long as the Windows session.
        uint flags = drive.Persistent ? CONNECT_UPDATE_PROFILE : CONNECT_TEMPORARY;

        int code = WNetAddConnection2W(ref resource, drive.Password, drive.Username, flags);
        return code == NO_ERROR
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
    }

    private static Result Disconnect(Drive drive)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";

        // Always CONNECT_UPDATE_PROFILE, never conditional on drive.Persistent. A
        // persistent mapping has to be cancelled with the same flag it was made with, or
        // only the live connection drops and Windows re-creates it at the next sign-in —
        // but the flag on the drive describes what it is *now*, not what was written to
        // the profile. Turn the setting off and disconnect, and a conditional flag would
        // leave the profile entry behind to resurrect the drive tomorrow.
        //
        // Passing it unconditionally is safe: for a drive that was never persistent there
        // is no profile entry, and removing one that does not exist does nothing.
        int code = WNetCancelConnection2W(local, CONNECT_UPDATE_PROFILE, fForce: true);

        return code == NO_ERROR
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(DescribeWNetError(code)));
    }

    /// <summary>
    /// Authenticates against the share without mapping it to a letter.
    /// </summary>
    /// <remarks>
    /// A null <c>lpLocalName</c> makes this a "deviceless" connection: Windows resolves
    /// the host, finds the share and checks the credentials, but claims no drive letter.
    /// That is what makes it safe to run against a half-finished form — the letter may
    /// still be in use, and the test deliberately says nothing about it either way. The
    /// session is dropped again straight afterwards so the test leaves nothing behind.
    /// </remarks>
    private static Result Test(Drive drive)
    {
        string remoteName = RemoteNameFor(drive);

        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpLocalName = null,
            lpRemoteName = remoteName,
            lpProvider = null,
        };

        int code = WNetAddConnection2W(ref resource, drive.Password, drive.Username, CONNECT_TEMPORARY);
        if (code != NO_ERROR)
        {
            return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
        }

        // Best-effort teardown. The question the user asked has already been answered by
        // this point, and a lingering deviceless session costs nothing and goes away at
        // sign-out — failing the test over it would misreport working credentials.
        WNetCancelConnection2W(remoteName, 0, fForce: false);

        return Result.Success();
    }

    private static string RemoteNameFor(Drive drive) => $@"\\{ToUncHost(drive.Host)}\{drive.Name}";

    /// <summary>
    /// Renders a host into the form a UNC path accepts.
    /// </summary>
    /// <remarks>
    /// IPv4 addresses and hostnames pass through untouched. An IPv6 literal cannot: a UNC
    /// path is a filesystem path and a colon is illegal in one. The Windows answer is the
    /// <c>ipv6-literal.net</c> encoding — colons become hyphens, the scope separator
    /// <c>%</c> becomes <c>s</c> — which the SMB redirector resolves without a DNS lookup.
    /// </remarks>
    internal static string ToUncHost(string host)
    {
        string candidate = host.Trim();

        // Accept the bracketed URL form too; that is how an IPv6 address is usually pasted.
        if (candidate.Length > 2 && candidate[0] == '[' && candidate[^1] == ']')
        {
            candidate = candidate[1..^1];
        }

        if (!candidate.Contains(':', StringComparison.Ordinal) ||
            !IPAddress.TryParse(candidate, out IPAddress? address) ||
            address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return candidate;
        }

        // Rendered from the parsed address rather than the typed text, so that the many
        // spellings of one address (FD00::5, fd00:0:0:0:0:0:0:5) map to a single literal.
        string literal = address.ToString()
            .Replace(':', '-')
            .Replace('%', 's');

        return $"{literal}.ipv6-literal.net";
    }

    private static async Task<Result> RunWithTimeoutAsync(
        Func<Result> work,
        Func<Result> timeoutError,
        Func<string, Result> failure,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ConnectionTimeoutMilliseconds);

        try
        {
            Task<Result> task = Task.Run(work, cts.Token);
            return await task.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            return cancellationToken.IsCancellationRequested
                ? failure("Operation canceled by user.")
                : timeoutError();
        }
        catch (Exception ex)
        {
            return failure($"Unexpected error: {ex.Message}");
        }
    }

    private static string DescribeWNetError(int code) => code switch
    {
        ERROR_ACCESS_DENIED => "Access denied. Check the username and password.",
        ERROR_ALREADY_ASSIGNED => "That drive letter is already in use.",
        ERROR_BAD_DEV_TYPE => "The network resource type is not correct.",
        ERROR_BAD_NETPATH => "The network path was not found.",
        ERROR_BAD_NET_NAME => "The network name cannot be found.",
        ERROR_INVALID_PASSWORD => "The password is incorrect.",
        ERROR_LOGON_FAILURE => "Logon failure: unknown user name or bad password.",
        ERROR_NO_NETWORK => "The network is not present or not started.",
        ERROR_NOT_CONNECTED => "The device is not currently connected.",
        ERROR_SESSION_CREDENTIAL_CONFLICT => "A conflicting credential set already exists for this server.",
        _ => new Win32Exception(code).Message,
    };

    // --- Win32 P/Invoke ---------------------------------------------------

    private const uint RESOURCETYPE_DISK = 0x00000001;
    private const uint CONNECT_UPDATE_PROFILE = 0x00000001;
    private const uint CONNECT_TEMPORARY = 0x00000004;

    private const int NO_ERROR = 0;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_ALREADY_ASSIGNED = 85;
    private const int ERROR_BAD_DEV_TYPE = 66;
    private const int ERROR_BAD_NETPATH = 53;
    private const int ERROR_BAD_NET_NAME = 67;
    private const int ERROR_INVALID_PASSWORD = 86;
    private const int ERROR_LOGON_FAILURE = 1326;
    private const int ERROR_NO_NETWORK = 1222;
    private const int ERROR_NOT_CONNECTED = 2250;
    private const int ERROR_SESSION_CREDENTIAL_CONFLICT = 1219;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public uint dwScope;
        public uint dwType;
        public uint dwDisplayType;
        public uint dwUsage;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpLocalName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpRemoteName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpComment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetAddConnection2W(
        ref NETRESOURCE lpNetResource,
        string? lpPassword,
        string? lpUserName,
        uint dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetCancelConnection2W(
        string lpName,
        uint dwFlags,
        [MarshalAs(UnmanagedType.Bool)] bool fForce);
}
