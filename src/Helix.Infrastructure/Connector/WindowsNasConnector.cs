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
    IHostReachability hostReachability,
    IDriveRouter driveRouter) : INasConnector
{
    private const int MountTimeoutMilliseconds = 30_000;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates =
        new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<LateMountOutcome>? MountSettledLate;

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            fresh: false,
            route => WithHostGateAsync(
                route,
                started => RunWithTimeoutAsync(
                    drive.Letter,
                    () => Connect(drive, route),
                    timeoutError: () => Result.Failure(DriveErrors.ConnectionTimedOut),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken,
                    started,
                    reportsMount: true),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            drive.Letter,
            () => Disconnect(drive),
            timeoutError: () => Result.Failure(DriveErrors.DisconnectionTimedOut),
            failure: message => Result.Failure(DriveErrors.FailedToDisconnect(message)),
            cancellationToken);

    public Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            fresh: true,
            route => WithHostGateAsync(
                route,
                started => RunWithTimeoutAsync(
                    drive.Letter,
                    () => Test(drive, route),
                    timeoutError: () => Result.Failure(DriveErrors.ConnectionTimedOut),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken,
                    started),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public string GetMountPath(string letter) => $"{letter.Trim().ToUpperInvariant()}:\\";

    public bool IsMountedFrom(Drive drive) => MountedFrom(drive, liveOnly: false);

    public bool IsLiveFrom(Drive drive) => MountedFrom(drive, liveOnly: true);

    private static bool MountedFrom(Drive drive, bool liveOnly)
    {
        if (string.IsNullOrWhiteSpace(drive.Letter))
        {
            return false;
        }

        string? remote = RemoteNameOf($"{drive.Letter.Trim().ToUpperInvariant()}:", liveOnly);
        if (remote is null)
        {
            return false;
        }

        if (!string.Equals(ShareOf(remote), drive.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string host = ToUncHost(drive.Host);

        return RemoteIs(remote, host, drive.Name) ||
               (drive.RemoteHost is not null && RemoteIs(remote, ToUncHost(drive.RemoteHost), drive.Name)) ||
               (HostSpelling.AlternateOf(host) is string alternate && RemoteIs(remote, alternate, drive.Name));
    }

    private static string? ShareOf(string remote)
    {
        string path = remote.TrimEnd('\\');

        if (!path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        int separator = path.IndexOf('\\', 2);

        return separator < 0 ? null : path[(separator + 1)..];
    }

    public bool HasOtherMountsOn(Drive drive)
    {
        string host = ToUncHost(drive.Host);
        string? away = drive.RemoteHost is null ? null : ToUncHost(drive.RemoteHost);
        string ownLetter = drive.Letter.Trim().ToUpperInvariant();
        List<string> others = [];

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

            string? remote = RemoteNameOf($"{letter}:", liveOnly: true);
            if (remote is null)
            {
                continue;
            }

            if (RemoteHostIs(remote, host) || (away is not null && RemoteHostIs(remote, away)))
            {
                return true;
            }

            others.Add(remote);
        }

        if (others.Count == 0 || HostSpelling.AlternateOf(host) is not string alternate)
        {
            return false;
        }

        return others.Any(remote => RemoteHostIs(remote, alternate));
    }

    public async Task<Result<IReadOnlyList<string>>> ListSharesAsync(
        string host,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!await hostReachability.ProbeNowAsync(host, cancellationToken))
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.HostUnreachable(host));
        }

        string uncHost = ToUncHost(host);
        SemaphoreSlim gate = _hostGates.GetOrAdd(uncHost, _ => new SemaphoreSlim(1, 1));

        bool held;

        try
        {
            held = await gate.WaitAsync(MountTimeoutMilliseconds, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.SharesNotListed("Operation canceled by user."));
        }

        Task<Result<IReadOnlyList<string>>> listing = Task.Run(
            () => ListShares(uncHost, username, password),
            CancellationToken.None);

        try
        {
            return await listing.WaitAsync(TimeSpan.FromMilliseconds(MountTimeoutMilliseconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.ConnectionTimedOut);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.SharesNotListed("Operation canceled by user."));
        }
        catch (Exception ex)
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.SharesNotListed($"Unexpected error: {ex.Message}"));
        }
        finally
        {
            if (held)
            {
                _ = listing.ContinueWith(
                    finished =>
                    {
                        _ = finished.Exception;
                        gate.Release();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    public IReadOnlyList<MappedShare> GetMappedShares()
    {
        List<MappedShare> mappings = [];

        for (char letter = 'A'; letter <= 'Z'; letter++)
        {
            string? remote = RemoteNameOf($"{letter}:");
            if (remote is null || !remote.StartsWith(@"\\", StringComparison.Ordinal))
            {
                continue;
            }

            string path = remote[2..].TrimEnd('\\');
            int separator = path.IndexOf('\\');
            if (separator <= 0 || separator == path.Length - 1)
            {
                continue;
            }

            string server = path[..separator];
            if (server.Contains('@', StringComparison.Ordinal))
            {
                continue;
            }

            mappings.Add(new MappedShare(letter.ToString(), FromUncHost(server), path[(separator + 1)..]));
        }

        return mappings;
    }

    private Result<IReadOnlyList<string>> ListShares(string uncHost, string username, string password)
    {
        if (HasLiveConnectionTo(uncHost))
        {
            return Enumerated(uncHost);
        }

        string ipc = ShareOn(uncHost, "IPC$");

        int code = AddConnection(local: null, ipc, username, password, CONNECT_TEMPORARY);

        if (code == ERROR_SESSION_CREDENTIAL_CONFLICT)
        {
            DropIdleServerSession(uncHost);

            code = AddConnection(local: null, ipc, username, password, CONNECT_TEMPORARY);
        }

        if (code == ERROR_SESSION_CREDENTIAL_CONFLICT)
        {
            logger.LogInformation("Listing shares: the server is held by another session; listing on that one.");

            Result<IReadOnlyList<string>> joined = Enumerated(uncHost);

            return joined.IsSuccess
                ? joined
                : Result.Failure<IReadOnlyList<string>>(DriveErrors.SessionConflict(DescribeWNetError(code)));
        }

        if (code != NO_ERROR)
        {
            return Result.Failure<IReadOnlyList<string>>(DriveErrors.SharesNotListed(DescribeWNetError(code)));
        }

        try
        {
            return Enumerated(uncHost);
        }
        finally
        {
            WNetCancelConnection2W(ipc, 0, fForce: false);
        }
    }

    private static Result<IReadOnlyList<string>> Enumerated(string uncHost)
    {
        int status = EnumerateDiskShares(uncHost, out List<string> shares);

        return status == NO_ERROR
            ? Result.Success<IReadOnlyList<string>>(shares)
            : Result.Failure<IReadOnlyList<string>>(DriveErrors.SharesNotListed(DescribeWNetError(status)));
    }

    private static int EnumerateDiskShares(string uncHost, out List<string> shares)
    {
        shares = [];

        int resume = 0;
        int status;

        do
        {
            status = NetShareEnum(
                $@"\\{uncHost}",
                1,
                out IntPtr buffer,
                MAX_PREFERRED_LENGTH,
                out int read,
                out _,
                ref resume);

            if (status != NO_ERROR && status != ERROR_MORE_DATA)
            {
                return status;
            }

            try
            {
                int size = Marshal.SizeOf<SHARE_INFO_1>();

                for (int i = 0; i < read; i++)
                {
                    SHARE_INFO_1 share = Marshal.PtrToStructure<SHARE_INFO_1>(buffer + (i * size));

                    bool isDisk = (share.shi1_type & STYPE_MASK) == STYPE_DISKTREE;
                    bool isSpecial = (share.shi1_type & STYPE_SPECIAL) != 0;

                    if (isDisk && !isSpecial && !string.IsNullOrEmpty(share.shi1_netname) &&
                        !share.shi1_netname.EndsWith('$'))
                    {
                        shares.Add(share.shi1_netname);
                    }
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                {
                    NetApiBufferFree(buffer);
                }
            }
        }
        while (status == ERROR_MORE_DATA);

        return NO_ERROR;
    }

    internal static string FromUncHost(string uncHost)
    {
        const string Ipv6Suffix = ".ipv6-literal.net";

        if (!uncHost.EndsWith(Ipv6Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return uncHost;
        }

        string literal = uncHost[..^Ipv6Suffix.Length]
            .Replace('-', ':')
            .Replace('s', '%');

        return IPAddress.TryParse(literal, out IPAddress? address) ? address.ToString() : uncHost;
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
        bool fresh,
        Func<DriveRoute, Task<Result>> work,
        CancellationToken cancellationToken)
    {
        Result<DriveRoute> route;

        try
        {
            route = await driveRouter.RouteAsync(drive, fresh, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(DriveErrors.FailedToConnect("Operation canceled by user."));
        }

        return route.IsSuccess
            ? await work(route.Value)
            : Result.Failure(route.Error);
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

    private Result Connect(Drive drive, DriveRoute route)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";
        string host = EffectiveHostFor(drive, route);
        string remote = ShareOn(host, drive.Name);

        uint flags = drive.Persistent ? CONNECT_UPDATE_PROFILE : CONNECT_TEMPORARY;

        int code = AddConnection(local, remote, drive.Username, drive.Password, flags);

        if (code == ERROR_ALREADY_ASSIGNED && MountedFrom(drive, liveOnly: true))
        {
            return Result.Success();
        }

        if ((code == ERROR_ALREADY_ASSIGNED || code == ERROR_DEVICE_ALREADY_REMEMBERED) &&
            ForgetStaleMapping(drive, local))
        {
            code = AddConnection(local, remote, drive.Username, drive.Password, flags);
        }

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

        if (reuse != ERROR_SESSION_CREDENTIAL_CONFLICT)
        {
            logger.LogWarning(
                "Drive {Letter}: mounting on the existing session failed — {Reason}",
                drive.Letter,
                DescribeWNetError(reuse));

            return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(reuse)));
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

    private bool ForgetStaleMapping(Drive drive, string local)
    {
        if (RemoteNameOf(local, liveOnly: true) is not null || !MountedFrom(drive, liveOnly: false))
        {
            return false;
        }

        if (WNetCancelConnection2W(local, CONNECT_UPDATE_PROFILE, fForce: false) != NO_ERROR)
        {
            return false;
        }

        logger.LogInformation(
            "Drive {Letter}: replaced a remembered mapping of its share that was not connected.",
            drive.Letter);

        return true;
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

    private static string EffectiveHostFor(Drive drive, DriveRoute route)
    {
        string host = ToUncHost(route.Host);

        if (route.IsRemote || !drive.ConnectByHostname || !HostSpelling.IsAddress(host))
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

            string? remote = RemoteNameOf($"{drive.Name[0]}:", liveOnly: true);

            if (remote is not null && remote.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string? RemoteNameOf(string localName, bool liveOnly = false)
    {
        var buffer = new char[RemoteNameLength];
        int length = buffer.Length;

        int code = WNetGetConnectionW(localName, buffer, ref length);

        if (code == ERROR_MORE_DATA && length > buffer.Length && length <= MaxPathLength)
        {
            buffer = new char[length];
            code = WNetGetConnectionW(localName, buffer, ref length);
        }

        bool known = code == NO_ERROR || (!liveOnly && code == ERROR_CONNECTION_UNAVAIL);
        if (!known)
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

    private static Result Test(Drive drive, DriveRoute route)
    {
        string host = EffectiveHostFor(drive, route);
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
        DriveRoute route,
        Func<Action<Task>, Task<Result>> work,
        Func<string, Result> failure,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = _hostGates.GetOrAdd(ToUncHost(route.Host), _ => new SemaphoreSlim(1, 1));

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
        Action<Task>? onStarted = null,
        bool reportsMount = false)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return failure("Operation canceled by user.");
        }

        Task<Result> task = Task.Run(work, CancellationToken.None);
        onStarted?.Invoke(task);

        try
        {
            return await task.WaitAsync(TimeSpan.FromMilliseconds(MountTimeoutMilliseconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(
                finished => ReportSettledLate(letter, finished, reportsMount),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return timeoutError();
        }
        catch (OperationCanceledException)
        {
            _ = task.ContinueWith(
                finished => ReportSettledLate(letter, finished, reportsMount),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return failure("Operation canceled by user.");
        }
        catch (Exception ex)
        {
            return failure($"Unexpected error: {ex.Message}");
        }
    }

    private void ReportSettledLate(string letter, Task<Result> finished, bool reportsMount)
    {
        bool mounted = !finished.IsFaulted && finished.Result.IsSuccess;

        string description = finished.IsFaulted
            ? finished.Exception?.GetBaseException().Message ?? "Unknown error."
            : mounted ? "It is now mounted." : finished.Result.Error.Description;

        logger.LogInformation(
            "Drive {Letter}: the mount that timed out finished afterwards - {Outcome}",
            letter,
            description);

        if (!reportsMount)
        {
            return;
        }

        try
        {
            MountSettledLate?.Invoke(this, new LateMountOutcome(letter, mounted, description));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Reporting the late mount of drive {Letter}: failed.", letter);
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

    private const int ERROR_CONNECTION_UNAVAIL = 1201;
    private const int ERROR_DEVICE_ALREADY_REMEMBERED = 1202;
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
    private const int RemoteNameLength = 1_024;

    private const int ERROR_MORE_DATA = 234;
    private const int MAX_PREFERRED_LENGTH = -1;
    private const uint STYPE_MASK = 0x000000FF;
    private const uint STYPE_DISKTREE = 0x00000000;
    private const uint STYPE_SPECIAL = 0x80000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHARE_INFO_1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string shi1_netname;
        public uint shi1_type;
        [MarshalAs(UnmanagedType.LPWStr)] public string shi1_remark;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(
        string servername,
        int level,
        out IntPtr bufptr,
        int prefmaxlen,
        out int entriesread,
        out int totalentries,
        ref int resume_handle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

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
