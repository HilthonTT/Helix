using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

[SupportedOSPlatform("windows")]
internal sealed class WindowsNasConnector(
    ILogger<WindowsNasConnector> logger,
    IHostReachability hostReachability) : INasConnector
{
    private const int MountTimeoutMilliseconds = 30_000;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            () => WithHostGateAsync(
                drive,
                started => RunWithTimeoutAsync(
                    drive.Letter,
                    () => Connect(drive),
                    timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken,
                    started),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            drive.Letter,
            () => Disconnect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToDisconnect("Disconnection timed out.")),
            failure: message => Result.Failure(DriveErrors.FailedToDisconnect(message)),
            cancellationToken);

    public Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            () => WithHostGateAsync(
                drive,
                started => RunWithTimeoutAsync(
                    drive.Letter,
                    () => Test(drive),
                    timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken,
                    started),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public string GetMountPath(string letter) => $"{letter.Trim().ToUpperInvariant()}:\\";

    public bool IsMountedFrom(Drive drive)
    {
        if (string.IsNullOrWhiteSpace(drive.Letter))
        {
            return false;
        }

        string? remote = RemoteNameOf($"{drive.Letter.Trim().ToUpperInvariant()}:");
        if (remote is null)
        {
            return false;
        }

        string host = ToUncHost(drive.Host);

        return RemoteIs(remote, host, drive.Name) ||
               (HostSpelling.AlternateOf(host) is string alternate && RemoteIs(remote, alternate, drive.Name));
    }

    public bool HasOtherMountsOn(Drive drive)
    {
        string host = ToUncHost(drive.Host);
        string? alternate = HostSpelling.AlternateOf(host);
        string ownLetter = drive.Letter.Trim().ToUpperInvariant();

        foreach (DriveInfo volume in DriveInfo.GetDrives())
        {
            if (volume.DriveType != DriveType.Network || volume.Name.Length == 0)
            {
                continue;
            }

            string letter = volume.Name[0].ToString().ToUpperInvariant();
            if (string.Equals(letter, ownLetter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? remote = RemoteNameOf($"{letter}:");
            if (remote is null)
            {
                continue;
            }

            if (RemoteHostIs(remote, host) ||
                (alternate is not null && RemoteHostIs(remote, alternate)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RemoteHostIs(string remote, string uncHost) =>
        remote.StartsWith($@"\\{uncHost}\", StringComparison.OrdinalIgnoreCase);

    private static bool RemoteIs(string remote, string uncHost, string share) =>
        string.Equals(
            remote.TrimEnd('\\'),
            ShareOn(uncHost, share),
            StringComparison.OrdinalIgnoreCase);

    private async Task<Result> WhenReachableAsync(
        Drive drive,
        Func<Task<Result>> work,
        CancellationToken cancellationToken)
    {
        if (!await hostReachability.IsReachableAsync(drive.Host, cancellationToken))
        {
            return Result.Failure(DriveErrors.HostUnreachable(drive.Host));
        }

        return await work();
    }

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

    public HashSet<string> GetConnectedLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.Name.Length > 0 && char.IsLetter(drive.Name[0]))
            {
                letters.Add(drive.Name[0].ToString().ToUpperInvariant());
            }
        }

        return letters;
    }

    private Result Connect(Drive drive)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";
        string host = EffectiveHostFor(drive);
        string remote = ShareOn(host, drive.Name);

        uint flags = drive.Persistent ? CONNECT_UPDATE_PROFILE : CONNECT_TEMPORARY;

        int code = AddConnection(local, remote, drive.Username, drive.Password, flags);
        if (code == NO_ERROR)
        {
            return Result.Success();
        }

        if (code != ERROR_SESSION_CREDENTIAL_CONFLICT)
        {
            return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
        }

        if (!HasLiveConnectionTo(host))
        {
            logger.LogInformation(
                "Drive {Letter}: clearing what looks like a leftover session for its server.",
                drive.Letter);

            DropIdleServerSession(host);

            int retry = AddConnection(local, remote, drive.Username, drive.Password, flags);
            if (retry == NO_ERROR)
            {
                return Result.Success();
            }

            if (retry != ERROR_SESSION_CREDENTIAL_CONFLICT)
            {
                logger.LogWarning(
                    "Drive {Letter}: would not mount with its own credentials — {Reason}",
                    drive.Letter,
                    DescribeWNetError(retry));

                return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(retry)));
            }

            logger.LogInformation(
                "Drive {Letter}: its server is held by a session with no drive letter; mounting on that one.",
                drive.Letter);
        }
        else
        {
            logger.LogInformation(
                "Drive {Letter}: its server is in use under other credentials; mounting on that session.",
                drive.Letter);
        }

        int reuse = AddConnection(local, remote, username: null, password: null, flags);
        if (reuse == NO_ERROR)
        {
            return Result.Success();
        }

        logger.LogWarning(
            "Drive {Letter}: the session its server is using would not take the mount — {Reason}",
            drive.Letter,
            DescribeWNetError(reuse));

        Result? alternate = TryAlternateSpelling(drive, local, host, flags);
        if (alternate is not null)
        {
            return alternate;
        }

        return Result.Failure(DriveErrors.SessionConflict(DescribeWNetError(code)));
    }

    private Result? TryAlternateSpelling(Drive drive, string local, string host, uint flags)
    {
        string? alternate = HostSpelling.AlternateOf(host);
        if (alternate is null)
        {
            return null;
        }

        int code = AddConnection(local, ShareOn(alternate, drive.Name), drive.Username, drive.Password, flags);
        if (code != NO_ERROR)
        {
            return null;
        }

        logger.LogInformation(
            "Drive {Letter}: mounted under its server's other name, which has a credential slot of its own.",
            drive.Letter);

        return Result.Success();
    }

    private static string EffectiveHostFor(Drive drive)
    {
        string host = ToUncHost(drive.Host);

        if (!drive.ConnectByHostname || !HostSpelling.IsAddress(host))
        {
            return host;
        }

        return HostSpelling.AlternateOf(host) ?? host;
    }

    private static bool HasLiveConnectionTo(string uncHost)
    {
        string prefix = $@"\\{uncHost}\";

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Network || drive.Name.Length == 0)
            {
                continue;
            }

            string? remote = RemoteNameOf($"{drive.Name[0]}:");

            if (remote is not null && remote.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? RemoteNameOf(string localName)
    {
        var buffer = new char[MaxPathLength];
        int length = buffer.Length;

        if (WNetGetConnectionW(localName, buffer, ref length) != NO_ERROR)
        {
            return null;
        }

        int end = Array.IndexOf(buffer, '\0');

        return end > 0 ? new string(buffer, 0, end) : null;
    }

    private static void DropIdleServerSession(string uncHost) =>
        WNetCancelConnection2W($@"\\{uncHost}", 0, fForce: false);

    private static int AddConnection(string? local, string remote, string? username, string? password, uint flags)
    {
        var resource = new NETRESOURCE
        {
            dwType = RESOURCETYPE_DISK,
            lpLocalName = local,
            lpRemoteName = remote,
            lpProvider = null,
        };

        return WNetAddConnection2W(ref resource, password, username, flags);
    }

    private static Result Disconnect(Drive drive)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";

        int code = WNetCancelConnection2W(local, CONNECT_UPDATE_PROFILE, fForce: true);

        return code == NO_ERROR
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(DescribeWNetError(code)));
    }

    private static Result Test(Drive drive)
    {
        string host = EffectiveHostFor(drive);
        string remoteName = ShareOn(host, drive.Name);

        int code = AddConnection(local: null, remoteName, drive.Username, drive.Password, CONNECT_TEMPORARY);

        if (code == ERROR_SESSION_CREDENTIAL_CONFLICT && !HasLiveConnectionTo(host))
        {
            DropIdleServerSession(host);

            code = AddConnection(local: null, remoteName, drive.Username, drive.Password, CONNECT_TEMPORARY);
        }

        if (code == ERROR_SESSION_CREDENTIAL_CONFLICT)
        {
            return Result.Failure(DriveErrors.SessionConflict(DescribeWNetError(code)));
        }

        if (code != NO_ERROR)
        {
            return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
        }

        WNetCancelConnection2W(remoteName, 0, fForce: false);

        return Result.Success();
    }

    private static string ShareOn(string uncHost, string shareName) => $@"\\{uncHost}\{shareName}";

    internal static string ToUncHost(string host)
    {
        string candidate = host.Trim();

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

        string literal = address.ToString()
            .Replace(':', '-')
            .Replace('%', 's');

        return $"{literal}.ipv6-literal.net";
    }

    private async Task<Result> WithHostGateAsync(
        Drive drive,
        Func<Action<Task>, Task<Result>> work,
        Func<string, Result> failure,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _hostGates.GetOrAdd(ToUncHost(drive.Host), _ => new SemaphoreSlim(1, 1));

        bool held;

        try
        {
            held = await gate.WaitAsync(MountTimeoutMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return failure("Operation canceled by user.");
        }

        Task completion = Task.CompletedTask;

        try
        {
            return await work(started => completion = started);
        }
        finally
        {
            if (held)
            {
                _ = completion.ContinueWith(
                    _ => gate.Release(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private async Task<Result> RunWithTimeoutAsync(
        string letter,
        Func<Result> work,
        Func<Result> timeoutError,
        Func<string, Result> failure,
        CancellationToken cancellationToken,
        Action<Task>? onStarted = null)
    {
        Task<Result> task = Task.Run(work, CancellationToken.None);
        onStarted?.Invoke(task);

        try
        {
            return await task.WaitAsync(TimeSpan.FromMilliseconds(MountTimeoutMilliseconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(
                finished => logger.LogInformation(
                    "Drive {Letter}: the mount that timed out finished afterwards - {Outcome}.",
                    letter,
                    finished.IsFaulted
                        ? finished.Exception?.GetBaseException().Message
                        : finished.Result.IsSuccess ? "it is now mounted" : finished.Result.Error.Description),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return timeoutError();
        }
        catch (OperationCanceledException)
        {
            _ = task.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);

            return failure("Operation canceled by user.");
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
        ERROR_SESSION_CREDENTIAL_CONFLICT =>
            "Windows is already signed in to this server with different credentials. Disconnect " +
            "every drive and Explorer window using it, or sign out of Windows, and try again.",
        _ => new Win32Exception(code).Message,
    };

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

    private const int MaxPathLength = 32_767;

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

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WNetGetConnectionW(
        string lpLocalName,
        [Out] char[] lpRemoteName,
        ref int lpnLength);
}
