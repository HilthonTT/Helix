namespace Helix.Domain.Drives;

public static class MountErrors
{
    public static readonly Error Canceled = Error.Problem(
        "Mount.Canceled",
        "The operation was canceled.");

    public static Error Unexpected(string message) => Error.Problem(
        "Mount.Unexpected",
        $"Unexpected error: {message}",
        message);

    public static Error System(string message) => Error.Problem(
        "Mount.System",
        message,
        message);

    public static Error OperationFailed(int code) => Error.Problem(
        "Mount.OperationFailed",
        $"The operation failed (code {code}).",
        code);

    public static readonly Error TimedOut = Error.Problem(
        "Mount.TimedOut",
        "Connection timed out.");

    public static readonly Error DisconnectTimedOut = Error.Problem(
        "Mount.DisconnectTimedOut",
        "Disconnection timed out.");

    public static readonly Error AccessDenied = Error.Problem(
        "Mount.AccessDenied",
        "Access denied. Check the username and password.");

    public static readonly Error LetterAlreadyAssigned = Error.Problem(
        "Mount.LetterAlreadyAssigned",
        "That drive letter is already in use.");

    public static readonly Error BadResourceType = Error.Problem(
        "Mount.BadResourceType",
        "The network resource type is not correct.");

    public static readonly Error NetworkPathNotFound = Error.Problem(
        "Mount.NetworkPathNotFound",
        "The network path was not found.");

    public static readonly Error NetworkNameNotFound = Error.Problem(
        "Mount.NetworkNameNotFound",
        "The network name cannot be found.");

    public static readonly Error PasswordIncorrect = Error.Problem(
        "Mount.PasswordIncorrect",
        "The password is incorrect.");

    public static readonly Error LogonFailure = Error.Problem(
        "Mount.LogonFailure",
        "Logon failure: unknown user name or bad password.");

    public static readonly Error NoNetwork = Error.Problem(
        "Mount.NoNetwork",
        "The network is not present or not started.");

    public static readonly Error NotConnected = Error.Problem(
        "Mount.NotConnected",
        "The device is not currently connected.");

    public static readonly Error CredentialConflict = Error.Problem(
        "Mount.CredentialConflict",
        "Windows is already signed in to this server with different credentials. Disconnect " +
        "every drive and Explorer window using it, or sign out of Windows, and try again.");

    public static readonly Error NfsClientMissing = Error.Problem(
        "Mount.NfsClientMissing",
        "Windows' Client for NFS is not installed. Turn on \"Services for NFS\" under Windows Features and try again.");

    public static readonly Error WebDavClientStopped = Error.Problem(
        "Mount.WebDavClientStopped",
        "Windows' WebDAV client is not running. Start the \"WebClient\" service and try again.");

    public static readonly Error AuthenticationFailed = Error.Problem(
        "Mount.AuthenticationFailed",
        "Authentication failed: unknown user name or bad password.");

    public static readonly Error MountPointBusy = Error.Problem(
        "Mount.MountPointBusy",
        "That mount point is already in use.");

    public static readonly Error ShareNotFound = Error.Problem(
        "Mount.ShareNotFound",
        "The share was not found on the server.");

    public static readonly Error ServerNotResponding = Error.Problem(
        "Mount.ServerNotResponding",
        "The server did not respond.");

    public static readonly Error ServerDown = Error.Problem(
        "Mount.ServerDown",
        "The server is down or unreachable.");

    public static readonly Error NetworkDown = Error.Problem(
        "Mount.NetworkDown",
        "The network is not available.");

    public static Error MountPointNotPrepared(string mountPoint, string message) => Error.Problem(
        "Mount.MountPointNotPrepared",
        $"Could not prepare the mount point '{mountPoint}': {message}",
        mountPoint,
        message);
}
