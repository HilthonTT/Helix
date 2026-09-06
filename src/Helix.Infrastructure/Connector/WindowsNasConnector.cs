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

/// <summary>
/// Maps network drives via the Win32 <c>mpr.dll</c> WNet APIs. Replaces the
/// previous <c>net.exe</c> shell-out which exposed NAS credentials in the
/// process command line and risked argument-injection through user-supplied
/// passwords.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsNasConnector(
    ILogger<WindowsNasConnector> logger,
    IHostReachability hostReachability) : INasConnector
{
    /// <summary>
    /// How long one mount may take before it is reported as timed out.
    /// </summary>
    /// <remarks>
    /// Generous on purpose, because the mount cannot be cancelled: <c>WNetAddConnection2</c>
    /// takes no token, so a timeout here only abandons the thread it is running on, and
    /// the mount then finishes on its own. At five seconds a NAS addressed by name — a
    /// NetBIOS or mDNS lookup, then the session — regularly came up a moment after the
    /// row had already been told it had timed out, which is the one message that is
    /// wrong on both counts. The absent-NAS case that the short value guarded against is
    /// answered by <see cref="IHostReachability"/> before the mount is attempted, in two
    /// seconds and once per host rather than once per drive.
    /// </remarks>
    private const int MountTimeoutMilliseconds = 30_000;

    /// <summary>
    /// One gate per server, so two shares of the same NAS are never mounted at the same
    /// instant.
    /// </summary>
    /// <remarks>
    /// Windows keeps a single credential context per server for the whole logon session,
    /// and establishing it is not atomic: hand the redirector thirteen shares of one NAS
    /// at once, as "connect all" and every drive group does, and the mounts that arrive
    /// while the first session is still being set up come back with
    /// <c>ERROR_SESSION_CREDENTIAL_CONFLICT</c>. Letting the first one through on its own
    /// turns the rest into cheap additions to a session that already exists.
    ///
    /// Keyed by the rendered UNC host rather than the typed one, so the several spellings
    /// of one address share a gate. The semaphores are never removed: a machine talks to
    /// a handful of NASes, and this object lives as long as the app does.
    /// </remarks>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _hostGates =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            () => WithHostGateAsync(
                drive,
                () => RunWithTimeoutAsync(
                    () => Connect(drive),
                    timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken),
                failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                cancellationToken),
            cancellationToken);

    public Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default) =>
        RunWithTimeoutAsync(
            () => Disconnect(drive),
            timeoutError: () => Result.Failure(DriveErrors.FailedToDisconnect("Disconnection timed out.")),
            failure: message => Result.Failure(DriveErrors.FailedToDisconnect(message)),
            cancellationToken);

    public Task<Result> TestAsync(Drive drive, CancellationToken cancellationToken = default) =>
        WhenReachableAsync(
            drive,
            () => WithHostGateAsync(
                drive,
                () => RunWithTimeoutAsync(
                    () => Test(drive),
                    timeoutError: () => Result.Failure(DriveErrors.FailedToConnect("Connection timed out.")),
                    failure: message => Result.Failure(DriveErrors.FailedToConnect(message)),
                    cancellationToken),
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

        // Either spelling of the server counts: the mapping may have been made under the
        // name while the drive carries the address, or the other way round, and both are
        // the same share on the same machine.
        string host = ToUncHost(drive.Host);

        return RemoteIs(remote, host, drive.Name) ||
               (HostSpelling.AlternateOf(host) is string alternate && RemoteIs(remote, alternate, drive.Name));
    }

    /// <summary>Whether a mapping's remote name is one particular share on one server.</summary>
    private static bool RemoteIs(string remote, string uncHost, string share) =>
        string.Equals(
            remote.TrimEnd('\\'),
            ShareOn(uncHost, share),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs the work only if the drive's server answers on an SMB port; otherwise reports
    /// it as unreachable without touching the redirector.
    /// </summary>
    /// <remarks>
    /// The reason the mount timeout can afford to be long. Away from the NAS's network a
    /// mount waits out the platform's own SMB timeout, and thirteen of them behind one
    /// gate would turn "connect all" into minutes; a two-second probe, cached and shared
    /// across the drives of one host, settles that first. The error carries
    /// <see cref="DriveErrors.HostUnreachableCode"/>, so a row connected by hand shows the
    /// same amber pill the watchdog's attempts do.
    /// </remarks>
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

    /// <summary>
    /// Maps one share, and deals with the one credential context Windows allows per
    /// server when the drive's own credentials are not the ones already in it.
    /// </summary>
    /// <remarks>
    /// A server gets one credential context per logon session, so a mount whose username
    /// and password differ from the ones that context was built with comes back
    /// <c>ERROR_SESSION_CREDENTIAL_CONFLICT</c> however correct they are. What to do
    /// about that depends entirely on whether anything is still using the context, and
    /// the cases pull in opposite directions:
    ///
    /// Something <b>is</b> using it — a share mapped by hand in Explorer, or the first of
    /// this NAS's thirteen drives, which is the case that made twelve of them fail at
    /// once — and there is no alternative to joining it. Windows will not hold a second
    /// credential set for that server while the first is in use, so the mount is retried
    /// with no credentials at all, which is how a share is asked onto an existing
    /// session. The drive comes up on whatever account the session belongs to.
    ///
    /// <b>Nothing</b> is using it, and the context is a leftover: a session outlives the
    /// last connection to it, invisible to <c>net use</c> and unreachable by Credential
    /// Manager, and the deviceless session <see cref="Test"/> opens is a common way to
    /// leave one behind. Joining that would be indefensible — it would mount the share
    /// while proving nothing whatsoever about the credentials the user just typed, so a
    /// password that is simply wrong would go on working until the leftover expired. It
    /// is dropped instead, and the drive's own credentials are given a real attempt.
    ///
    /// Which of those it is, is decided by <see cref="HasLiveConnectionTo"/>, and that
    /// question can only be answered for the sessions that carry a drive letter. A
    /// deviceless one — a backup client that mounts the NAS at boot, an Explorer window
    /// left on a UNC path, a leftover of Helix's own <see cref="Test"/> — looks exactly
    /// like nothing at all, so the drop is refused by the process holding it and the
    /// honest retry conflicts again. That second conflict is the tell: a leftover would
    /// have been gone. Every drive of a NAS failing at boot because a backup had already
    /// opened it is what this costs, so the third case joins the session it cannot see
    /// rather than failing, on exactly the reasoning as the first.
    ///
    /// The wrong-password guarantee survives that, because it only ever applied to the
    /// genuinely idle case: there the drop succeeds and the retry is a real
    /// authentication whose logon failure is reported as one. A session that refuses to
    /// be dropped was never going to test anything.
    ///
    /// Dropping it is only ever done on the idle branch: on the other,
    /// <c>WNetCancelConnection2</c> against a server name would take down the user's
    /// Explorer mappings and every Helix drive already mounted on that NAS. Even there it
    /// is unforced, so an open handle refuses the cancel rather than losing anything.
    /// </remarks>
    private Result Connect(Drive drive)
    {
        string local = $"{drive.Letter.ToUpperInvariant()}:";
        string host = EffectiveHostFor(drive);
        string remote = ShareOn(host, drive.Name);

        // CONNECT_UPDATE_PROFILE writes the mapping into the user profile, so Explorer
        // restores it at sign-in without Helix running. CONNECT_TEMPORARY is the opposite
        // and stays the default: the mapping lives exactly as long as the Windows session.
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

            // Whatever the honest attempt said, including a plain logon failure — which
            // is the answer the user is owed when the stored password is wrong.
            if (retry != ERROR_SESSION_CREDENTIAL_CONFLICT)
            {
                logger.LogWarning(
                    "Drive {Letter}: would not mount with its own credentials — {Reason}",
                    drive.Letter,
                    DescribeWNetError(retry));

                return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(retry)));
            }

            // Still conflicting after the drop, so the session was never idle: it is held
            // by something with no drive letter, which is invisible to the check above —
            // a backup client that mounts the NAS at boot, an Explorer window sitting on
            // a UNC path, a deviceless leftover of Helix's own Test. Fall through and
            // join it rather than giving up on a server that is plainly reachable.
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

        // Last resort. The session holds the server under the spelling being used, and
        // Windows keys credentials per spelling, so the other one is an untouched slot
        // this drive's own credentials can have to themselves.
        Result? alternate = TryAlternateSpelling(drive, local, host, flags);
        if (alternate is not null)
        {
            return alternate;
        }

        // Reported as the conflict rather than as whatever the fallbacks tripped over: the
        // session in use is the reason this drive is not mounted, and the later attempts'
        // errors only describe what that session would not let us do.
        return Result.Failure(DriveErrors.FailedToConnect(DescribeWNetError(code)));
    }

    /// <summary>
    /// Mounts under the server's other name — its hostname if the drive names an address,
    /// its address if the drive names a host — or null when there is no other name to try
    /// or it did not work either.
    /// </summary>
    /// <remarks>
    /// Windows hands out one credential context per server <i>name string</i>, so the two
    /// spellings of one NAS are two slots. Everything above this has already failed: the
    /// drive's own credentials conflicted, the session would not be dropped, and joining
    /// it was refused. Under the other spelling there is no session to conflict with, so
    /// the drive's real credentials get a clean attempt — which is strictly better than
    /// the join even when both would work, because a wrong password fails here rather
    /// than mounting on somebody else's account.
    ///
    /// Doing this unasked is defensible only as a last resort. It costs a second
    /// credential context to a NAS that already has one, which is the state that produces
    /// these conflicts in the first place: paying that to rescue a drive that is otherwise
    /// not mounting is worth it, paying it routinely is not. A user who wants it routinely
    /// turns on <see cref="Drive.ConnectByHostname"/> and gets it before the conflict
    /// rather than after.
    ///
    /// A failure returns null rather than an error of its own, so the caller still reports
    /// the conflict that actually stopped the drive.
    /// </remarks>
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

    /// <summary>
    /// The host to mount under: the one the user typed, unless the drive asked for the
    /// server's name and typed an address.
    /// </summary>
    /// <remarks>
    /// A lookup that answers nothing leaves the address as typed. The switch is a way
    /// round a collision, not a requirement — failing the mount because DNS was quiet
    /// would turn an optional improvement into a new way to lose a drive.
    /// </remarks>
    private static string EffectiveHostFor(Drive drive)
    {
        string host = ToUncHost(drive.Host);

        if (!drive.ConnectByHostname || !HostSpelling.IsAddress(host))
        {
            return host;
        }

        return HostSpelling.AlternateOf(host) ?? host;
    }

    /// <summary>
    /// Whether any drive letter is currently mounted from <paramref name="uncHost"/>.
    /// </summary>
    /// <remarks>
    /// The question being asked is "would dropping this server's session cost anybody
    /// anything", and a mapped letter is the form that costs the most. A deviceless
    /// connection held by another process is not visible here, which is why the cancel
    /// that follows a false answer is left unforced — that case refuses rather than
    /// breaking something this cannot see — and why <see cref="Connect"/> treats a
    /// conflict that survives the cancel as proof of a holder this could not see, rather
    /// than as a final answer.
    /// </remarks>
    private static bool HasLiveConnectionTo(string uncHost)
    {
        // Two leading backslashes, as WNetGetConnection reports it, and a trailing one so
        // that \\NAS cannot match a letter mounted from \\NAS2.
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

    /// <summary>Resolves what a mapped letter points at, or null if it is not a mapping.</summary>
    private static string? RemoteNameOf(string localName)
    {
        var buffer = new char[MaxPathLength];
        int length = buffer.Length;

        if (WNetGetConnectionW(localName, buffer, ref length) != NO_ERROR)
        {
            return null;
        }

        // Read to the terminator rather than trusting the returned length, which counts
        // it on some paths and not others.
        int end = Array.IndexOf(buffer, '\0');

        return end > 0 ? new string(buffer, 0, end) : null;
    }

    /// <summary>
    /// Drops a server session that no mapped letter is using, so the next mount has to
    /// authenticate for real. Best effort — an in-use session refuses and is left alone.
    /// </summary>
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
        string host = EffectiveHostFor(drive);
        string remoteName = ShareOn(host, drive.Name);

        int code = AddConnection(local: null, remoteName, drive.Username, drive.Password, CONNECT_TEMPORARY);

        // The same leftover-session problem the mount has, and worse here: a test that
        // answers from a session nobody is using has tested nothing. Cleared and asked
        // again, so the answer is about the credentials on screen.
        if (code == ERROR_SESSION_CREDENTIAL_CONFLICT && !HasLiveConnectionTo(host))
        {
            DropIdleServerSession(host);

            code = AddConnection(local: null, remoteName, drive.Username, drive.Password, CONNECT_TEMPORARY);
        }

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

    /// <summary>The UNC path of one share on one server.</summary>
    private static string ShareOn(string uncHost, string shareName) => $@"\\{uncHost}\{shareName}";

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

    /// <summary>
    /// Runs the work with the server's gate held, so only one mount to a given NAS is in
    /// flight at a time.
    /// </summary>
    /// <remarks>
    /// The wait is capped at the same timeout one mount gets. Nothing holds the gate for
    /// longer than a single attempt, so the cap only bites when that attempt is hanging
    /// on a NAS that stopped answering after the reachability probe let it through — and
    /// queueing behind it is then the wrong answer, because "connect all" would take the
    /// timeout per drive instead of once in total. Going ahead ungated costs nothing
    /// there: the race the gate exists for cannot happen on a server that is not
    /// answering.
    ///
    /// The cap used to be five seconds, which a NAS addressed by name exceeded on its
    /// first mount often enough that the other twelve drives burst through ungated into
    /// the very credential conflicts the gate is for.
    /// </remarks>
    private async Task<Result> WithHostGateAsync(
        Drive drive,
        Func<Task<Result>> work,
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

        try
        {
            return await work();
        }
        finally
        {
            if (held)
            {
                gate.Release();
            }
        }
    }

    private static async Task<Result> RunWithTimeoutAsync(
        Func<Result> work,
        Func<Result> timeoutError,
        Func<string, Result> failure,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(MountTimeoutMilliseconds);

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
        ERROR_SESSION_CREDENTIAL_CONFLICT =>
            "Windows is already signed in to this server with different credentials. Disconnect " +
            "every drive and Explorer window using it, or sign out of Windows, and try again.",
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

    /// <summary>Buffer size for <c>WNetGetConnection</c>, in characters.</summary>
    private const int MaxPathLength = 260;

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
