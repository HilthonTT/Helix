using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using SharedKernel;
using System.ComponentModel;
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
internal sealed class NasConnector : INasConnector
{
    private const int ConnectionTimeoutMilliseconds = 5_000;

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Connect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
            cancellationToken);

    public Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Disconnect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToDisconnect("Disconnection timed out.")),
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
            lpRemoteName = $@"\\{drive.IpAddress}\{drive.Name}",
            lpProvider = null,
        };

        int code = WNetAddConnection2W(ref resource, drive.Password, drive.Username, CONNECT_TEMPORARY);
        return code == NO_ERROR
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
    }

    private static Result Disconnect(Drive drive)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";
        int code = WNetCancelConnection2W(local, 0, fForce: true);

        return code == NO_ERROR
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(DescribeWNetError(code)));
    }

    private static async Task<Result> RunWithTimeoutAsync(
        Func<Result> work,
        Func<Result> timeoutError,
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
                ? Result.Failure(DriveErrors.FailedToConnect("Operation canceled by user."))
                : timeoutError();
        }
        catch (Exception ex)
        {
            return Result.Failure(DriveErrors.FailedToConnect($"Unexpected error: {ex.Message}"));
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
