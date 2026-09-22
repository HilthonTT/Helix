using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

public sealed class DiagnoseDrive(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector,
    IHostDiagnostics hostDiagnostics) : IHandler
{
    public sealed record Request(Guid DriveId);

    public async Task<Result<DriveDiagnosis>> Handle(
        Request request,
        CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<DriveDiagnosis>(AuthenticationErrors.InvalidPermissions);
        }

        Drive? drive = await driveRepository.GetByIdAsNoTrackingAsync(request.DriveId, cancellationToken);
        if (drive is null)
        {
            return Result.Failure<DriveDiagnosis>(DriveErrors.NotFound(request.DriveId));
        }

        if (drive.UserId != loggedInUser.UserId)
        {
            return Result.Failure<DriveDiagnosis>(AuthenticationErrors.InvalidPermissions);
        }

        string host = drive.Host;
        HostProbe probe = await hostDiagnostics.ProbeAsync(host, cancellationToken);

        if (!probe.Reachable && drive.RemoteHost is not null)
        {
            HostProbe away = await hostDiagnostics.ProbeAsync(drive.RemoteHost, cancellationToken);

            if (away.Reachable)
            {
                host = drive.RemoteHost;
                probe = away;
            }
        }

        List<DiagnosticResult> steps =
        [
            Resolution(probe),
            Reachability(host, probe),
            await ShareAndCredentialsAsync(drive, host, probe, cancellationToken),
            LetterAvailability(drive),
        ];

        return Result.Success(new DriveDiagnosis(drive.Id, drive.Letter, drive.Name, host, steps));
    }

    private static DiagnosticResult Resolution(HostProbe probe) => probe.AlternateSpelling is null
        ? new DiagnosticResult(
            DiagnosticStep.HostResolution,
            DiagnosticOutcome.Warned,
            DiagnosticFinding.HostSpellingUnknown)
        : new DiagnosticResult(
            DiagnosticStep.HostResolution,
            DiagnosticOutcome.Passed,
            DiagnosticFinding.HostSpellingResolved,
            probe.AlternateSpelling);

    private static DiagnosticResult Reachability(string host, HostProbe probe) => probe.Reachable
        ? new DiagnosticResult(
            DiagnosticStep.HostReachable,
            DiagnosticOutcome.Passed,
            DiagnosticFinding.PortOpen,
            probe.OpenPort?.ToString())
        : new DiagnosticResult(
            DiagnosticStep.HostReachable,
            DiagnosticOutcome.Failed,
            DiagnosticFinding.HostSilent,
            host);

    private async Task<DiagnosticResult> ShareAndCredentialsAsync(
        Drive drive,
        string host,
        HostProbe probe,
        CancellationToken cancellationToken)
    {
        if (!probe.Reachable)
        {
            return new DiagnosticResult(
                DiagnosticStep.ShareAndCredentials,
                DiagnosticOutcome.Skipped,
                DiagnosticFinding.None);
        }

        if (nasConnector.IsMountedFrom(drive))
        {
            return new DiagnosticResult(
                DiagnosticStep.ShareAndCredentials,
                DiagnosticOutcome.Passed,
                DiagnosticFinding.AlreadyMounted);
        }

        bool joinsAnotherSession = nasConnector.HasOtherMountsOn(drive);

        Result test = await nasConnector.TestAsync(drive, cancellationToken);
        if (test.IsSuccess)
        {
            return joinsAnotherSession
                ? new DiagnosticResult(
                    DiagnosticStep.ShareAndCredentials,
                    DiagnosticOutcome.Warned,
                    DiagnosticFinding.CredentialsUntested,
                    host)
                : new DiagnosticResult(
                    DiagnosticStep.ShareAndCredentials,
                    DiagnosticOutcome.Passed,
                    DiagnosticFinding.CredentialsAccepted);
        }

        DiagnosticFinding finding = test.Error.Code == DriveErrors.SessionConflictCode
            ? DiagnosticFinding.SessionConflict
            : DiagnosticFinding.CredentialsRejected;

        return new DiagnosticResult(
            DiagnosticStep.ShareAndCredentials,
            DiagnosticOutcome.Failed,
            finding,
            test.Error.Description);
    }

    private DiagnosticResult LetterAvailability(Drive drive)
    {
        if (!nasConnector.IsConnected(drive.Letter))
        {
            return new DiagnosticResult(
                DiagnosticStep.LetterAvailable,
                DiagnosticOutcome.Passed,
                DiagnosticFinding.LetterFree,
                drive.Letter);
        }

        return nasConnector.IsMountedFrom(drive)
            ? new DiagnosticResult(
                DiagnosticStep.LetterAvailable,
                DiagnosticOutcome.Passed,
                DiagnosticFinding.LetterHeldByThisShare,
                drive.Letter)
            : new DiagnosticResult(
                DiagnosticStep.LetterAvailable,
                DiagnosticOutcome.Failed,
                DiagnosticFinding.LetterHeldByAnother,
                drive.Letter);
    }
}
