#if MACCATALYST
using Foundation;
using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Helix.Infrastructure.Connector;

/// <summary>
/// Mounts SMB shares through <c>NetFSMountURLSync</c>, the same NetFS entry point
/// Finder uses for "Connect to Server".
/// </summary>
/// <remarks>
/// The password is handed over as its own CFString argument rather than embedded in
/// the URL or a command line, which keeps it out of <c>ps</c> output and shell
/// history — the same reason the Windows side uses the WNet APIs instead of shelling
/// out to <c>net.exe</c>. Do not "simplify" this to <c>mount_smbfs //user:pass@host</c>.
///
/// macOS has no drive letters, so a drive's letter names a directory under
/// <see cref="MountRoot"/> instead: <c>Z:</c> on Windows is <c>~/Helix Drives/Z</c>
/// here. That keeps the persisted domain model identical across the two platforms and
/// makes "is it connected?" a plain mount-point lookup.
///
/// Mounting a network volume is not possible from inside the App Sandbox, so the
/// Catalyst head ships with the sandbox disabled — see Platforms/MacCatalyst/Entitlements.plist.
/// </remarks>
[SupportedOSPlatform("maccatalyst")]
internal sealed class MacNasConnector : INasConnector
{
    private const int ConnectionTimeoutMilliseconds = 5_000;

    /// <summary>Directory the mount points live under, one per drive letter.</summary>
    private static readonly string MountRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Helix Drives");

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

        return GetConnectedLetters().Contains(Normalize(letter));
    }

    /// <summary>
    /// The letters whose mount point is currently a live mounted volume. Reads the
    /// mount table rather than the filesystem, so a leftover empty directory from a
    /// failed mount does not read as connected.
    /// </summary>
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

    /// <summary>
    /// Maps <c>~/Helix Drives/Z</c> back to <c>Z</c>, or null for any other volume.
    /// </summary>
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

    /// <summary>
    /// Mounts the share, checks it, and unmounts it again — leaving neither the volume
    /// mounted nor the drive letter's own mount point touched.
    /// </summary>
    /// <remarks>
    /// The Windows side answers this with a deviceless WNet connection, which has no
    /// NetFS equivalent: the only way to know NetFS accepts the host, share and password
    /// is to let it mount. So the mount goes to a scratch directory beside the real ones
    /// and is torn down immediately, which keeps a test on a half-finished form from
    /// disturbing whatever is currently mounted at the drive's own letter.
    /// </remarks>
    private static Result Test(Drive drive)
    {
        string mountPoint = Path.Combine(MountRoot, $".test-{Guid.NewGuid():N}");

        try
        {
            return Mount(drive, mountPoint);
        }
        finally
        {
            // Best-effort teardown; the caller already has its answer either way.
            unmount(mountPoint, MntForce);

            try
            {
                Directory.Delete(mountPoint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Helix: could not remove the test mount point: {ex.Message}");
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

        // The share name is the drive's Name, matching the Windows side's \\host\name.
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

        // MNT_FORCE (0x080000) mirrors the Windows side's fForce: a share whose server
        // has gone away otherwise refuses to unmount and the row stays stuck.
        if (unmount(mountPoint, MntForce) == 0)
        {
            return Result.Success();
        }

        int errno = Marshal.GetLastPInvokeError();

        // EINVAL means "not a mount point" — already disconnected, which is the
        // outcome the caller asked for.
        return errno == Einval
            ? Result.Success()
            : Result.Failure(DriveErrors.FailedToDisconnect(DescribeErrno(errno)));
    }

    private static string MountPointFor(string letter) => Path.Combine(MountRoot, Normalize(letter));

    private static string Normalize(string letter) => letter.Trim().ToUpperInvariant();

    /// <summary>
    /// Renders a host into the authority of an <c>smb://</c> URL.
    /// </summary>
    /// <remarks>
    /// IPv4 addresses and hostnames go in as they are. An IPv6 literal has to be
    /// bracketed, or the colons in the address are read as the port separator and the URL
    /// silently addresses the wrong thing. Where Windows needs <c>ipv6-literal.net</c>
    /// because a UNC path cannot hold a colon, a URL only needs the brackets.
    /// </remarks>
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

    // --- native interop ---------------------------------------------------

    private const int MntForce = 0x00080000;

    private const int Enoent = 2;
    private const int Eacces = 13;
    private const int Ebusy = 16;
    private const int Einval = 22;
    private const int Enetdown = 50;
    private const int Etimedout = 60;
    private const int Ehostdown = 64;
    private const int Eauth = 80;

    /// <summary>
    /// NetFS mount. CFURLRef/CFStringRef are toll-free bridged with NSUrl/NSString, so
    /// the managed handles can be passed straight through.
    /// </summary>
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
