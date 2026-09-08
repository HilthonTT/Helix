#if MACCATALYST
using Foundation;
using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

[SupportedOSPlatform("maccatalyst")]
internal sealed class MacNasConnector : INasConnector
{
    private const int MountTimeoutMilliseconds = 30_000;

    private static readonly string MountRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Helix Drives");

    private readonly ILogger<MacNasConnector> _logger;
    private readonly IHostReachability _hostReachability;

    public event EventHandler<LateMountOutcome>? MountSettledLate;

    public MacNasConnector(ILogger<MacNasConnector> logger, IHostReachability hostReachability)
    {
        _logger = logger;
        _hostReachability = hostReachability;
    }

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            fresh: false,
            () => RunWithTimeoutAsync(
                drive.Letter,
                () => Connect(drive),
                timeoutError: () => Result.Failure(DriveErrors.ConnectionTimedOut),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken,
                reportsMount: true),
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
            () => RunWithTimeoutAsync(
                drive.Letter,
                () => Test(drive),
                timeoutError: () => Result.Failure(DriveErrors.ConnectionTimedOut),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public string GetMountPath(string letter) => MountPointFor(letter);

    public bool IsMountedFrom(Drive drive) => IsConnected(drive.Letter);

    public bool HasOtherMountsOn(Drive drive) => false;

    private async Task<Result> WhenReachableAsync(
        Drive drive,
        bool fresh,
        Func<Task<Result>> work,
        CancellationToken cancellationToken)
    {
        bool reachable = fresh
            ? await _hostReachability.ProbeNowAsync(drive.Host, cancellationToken)
            : await _hostReachability.IsReachableAsync(drive.Host, cancellationToken);

        if (!reachable)
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

        return GetConnectedLetters().Contains(Normalize(letter));
    }

    public HashSet<string> GetConnectedLetters()
    {
        var letters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DriveInfo volume in DriveInfo.GetDrives())
        {
            string? letter = LetterForMountPoint(volume.Name);
            if (letter is not null)
            {
                letters.Add(letter);
            }
        }

        return letters;
    }

    private static string? LetterForMountPoint(string mountPoint)
    {
        string trimmed = mountPoint.TrimEnd(Path.DirectorySeparatorChar);
        string root = MountRoot.TrimEnd(Path.DirectorySeparatorChar);

        if (!trimmed.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = trimmed[(root.Length + 1)..];

        return remainder.Length == 1 && char.IsLetter(remainder[0])
            ? remainder.ToUpperInvariant()
            : null;
    }

    private static Result Connect(Drive drive) => Mount(drive, MountPointFor(drive.Letter));

    private Result Test(Drive drive)
    {
        string mountPoint = Path.Combine(MountRoot, $".test-{Guid.NewGuid():N}");

        try
        {
            return Mount(drive, mountPoint);
        }
        finally
        {
            unmount(mountPoint, MntForce);

            try
            {
                Directory.Delete(mountPoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not remove the test mount point {MountPoint}.", mountPoint);
            }
        }
    }

    private static Result Mount(Drive drive, string mountPoint)
    {
        try
        {
            Directory.CreateDirectory(mountPoint);
        }
        catch (Exception ex)
        {
            return Result.Failure(DriveErrors.FailedToConnect(
                $"Could not prepare the mount point '{mountPoint}': {ex.Message}"));
        }

        var url = new NSUrl($"smb://{ToUrlHost(drive.Host)}/{Uri.EscapeDataString(drive.Name)}");
        var mountPath = NSUrl.FromFilename(mountPoint);
        var user = new NSString(drive.Username);
        var password = new NSString(drive.Password);

        IntPtr mountedPaths = IntPtr.Zero;

        try
        {
            int code = NetFSMountURLSync(
                url.Handle,
                mountPath.Handle,
                user.Handle,
                password.Handle,
                IntPtr.Zero,
                IntPtr.Zero,
                out mountedPaths);

            return code == 0
                ? Result.Success()
                : Result.Failure(DriveErrors.FailedToConnect(DescribeNetFsError(code)));
        }
        finally
        {
            if (mountedPaths != IntPtr.Zero)
            {
                CFRelease(mountedPaths);
            }

            url.Dispose();
            mountPath.Dispose();
            user.Dispose();
            password.Dispose();
        }
    }

    private static Result Disconnect(Drive drive)
    {
        string mountPoint = MountPointFor(drive.Letter);

        if (unmount(mountPoint, MntForce) == 0)
        {
            return Result.Success();
        }

        int errno = Marshal.GetLastPInvokeError();

        return errno == Einval
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(DescribeErrno(errno)));
    }

    private static string MountPointFor(string letter) => Path.Combine(MountRoot, Normalize(letter));

    private static string Normalize(string letter) => letter.Trim().ToUpperInvariant();

    internal static string ToUrlHost(string host)
    {
        string candidate = host.Trim();

        if (candidate.Length > 2 && candidate[0] == '[' && candidate[^1] == ']')
        {
            return candidate;
        }

        bool isIpv6 = candidate.Contains(':', StringComparison.Ordinal) &&
                      IPAddress.TryParse(candidate, out IPAddress? address) &&
                      address.AddressFamily == AddressFamily.InterNetworkV6;

        return isIpv6 ? $"[{candidate}]" : candidate;
    }

    private async Task<Result> RunWithTimeoutAsync(
        string letter,
        Func<Result> work,
        Func<Result> timeoutError,
        Func<string, Result> failure,
        CancellationToken cancellationToken,
        bool reportsMount = false)
    {
        Task<Result> task = Task.Run(work, CancellationToken.None);

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
            _ = task.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);

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

        _logger.LogInformation(
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
            _logger.LogError(ex, "Reporting the late mount of drive {Letter}: failed.", letter);
        }
    }

    private static string DescribeNetFsError(int code) => code switch
    {
        Eacces => "Access denied. Check the username and password.",
        Eauth => "Authentication failed: unknown user name or bad password.",
        Ebusy => "That mount point is already in use.",
        Enoent => "The share was not found on the server.",
        Etimedout => "The server did not respond.",
        Ehostdown => "The server is down or unreachable.",
        Enetdown => "The network is not available.",
        _ => DescribeErrno(code),
    };

    private static string DescribeErrno(int code)
    {
        IntPtr message = strerror(code);

        return message == IntPtr.Zero
            ? $"The operation failed (code {code})."
            : Marshal.PtrToStringUTF8(message) ?? $"The operation failed (code {code}).";
    }

    private const int MntForce = 0x00080000;

    private const int Enoent = 2;
    private const int Eacces = 13;
    private const int Ebusy = 16;
    private const int Einval = 22;
    private const int Enetdown = 50;
    private const int Etimedout = 60;
    private const int Ehostdown = 64;
    private const int Eauth = 80;

    [DllImport("/System/Library/Frameworks/NetFS.framework/NetFS")]
    private static extern int NetFSMountURLSync(
        IntPtr url,
        IntPtr mountpath,
        IntPtr user,
        IntPtr password,
        IntPtr openOptions,
        IntPtr mountOptions,
        out IntPtr mountpoints);

    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static extern void CFRelease(IntPtr cf);

    [DllImport("libc", SetLastError = true)]
    private static extern int unmount([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc")]
    private static extern IntPtr strerror(int code);
}
#endif
