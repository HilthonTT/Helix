using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class DiagnoseDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly DiagnoseDrive _diagnoseDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IHostDiagnostics _hostDiagnosticsMock;

    private readonly Drive _drive;

    public DiagnoseDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _hostDiagnosticsMock = Substitute.For<IHostDiagnostics>();

        _diagnoseDrive = new(
            _driveRepositoryMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _hostDiagnosticsMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock
            .GetByIdAsNoTrackingAsync(_drive.Id, Arg.Any<CancellationToken>())
            .Returns(_drive);

        _hostDiagnosticsMock
            .ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HostProbe(true, 445, "NAS"));

        _nasConnectorMock
            .TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    private static DiagnosticResult StepOf(DriveDiagnosis diagnosis, DiagnosticStep step) =>
        diagnosis.Steps.Single(s => s.Step == step);

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenDriveBelongsToAnotherUser()
    {
        _loggedInUserMock.UserId.Returns(Guid.NewGuid());

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await _nasConnectorMock.DidNotReceive().TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenDriveDoesNotExist()
    {
        Guid missing = Guid.NewGuid();

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(missing));

        result.Error.Should().Be(DriveErrors.NotFound(missing));
    }

    [Fact]
    public async Task Handle_Should_ReportEveryStep()
    {
        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        result.IsSuccess.Should().BeTrue();
        result.Value.Steps.Select(s => s.Step).Should().Equal(
            DiagnosticStep.HostResolution,
            DiagnosticStep.HostReachable,
            DiagnosticStep.ShareAndCredentials,
            DiagnosticStep.LetterAvailable);
    }

    [Fact]
    public async Task Handle_Should_ReportHealthy_WhenEveryCheckPasses()
    {
        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        result.Value.HasFailure.Should().BeFalse();
        StepOf(result.Value, DiagnosticStep.HostReachable).Finding.Should().Be(DiagnosticFinding.PortOpen);
        StepOf(result.Value, DiagnosticStep.ShareAndCredentials).Finding
            .Should().Be(DiagnosticFinding.CredentialsAccepted);
        StepOf(result.Value, DiagnosticStep.LetterAvailable).Finding.Should().Be(DiagnosticFinding.LetterFree);
    }

    [Fact]
    public async Task Handle_Should_WarnAboutSpelling_WhenNoAlternateResolves()
    {
        _hostDiagnosticsMock
            .ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HostProbe(true, 445, null));

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult step = StepOf(result.Value, DiagnosticStep.HostResolution);

        step.Outcome.Should().Be(DiagnosticOutcome.Warned);
        step.Finding.Should().Be(DiagnosticFinding.HostSpellingUnknown);
        result.Value.HasFailure.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_SkipTheShareCheck_WhenTheHostIsSilent()
    {
        _hostDiagnosticsMock
            .ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HostProbe(false, null, null));

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        StepOf(result.Value, DiagnosticStep.HostReachable).Finding.Should().Be(DiagnosticFinding.HostSilent);
        StepOf(result.Value, DiagnosticStep.ShareAndCredentials).Outcome
            .Should().Be(DiagnosticOutcome.Skipped);
        result.Value.HasFailure.Should().BeTrue();

        await _nasConnectorMock.DidNotReceive().TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_StillCheckTheLetter_WhenTheHostIsSilent()
    {
        _hostDiagnosticsMock
            .ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new HostProbe(false, null, null));

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        StepOf(result.Value, DiagnosticStep.LetterAvailable).Outcome.Should().Be(DiagnosticOutcome.Passed);
    }

    [Fact]
    public async Task Handle_Should_WarnThePasswordWasNotChecked_WhenAnotherDriveHoldsTheSession()
    {
        _nasConnectorMock.HasOtherMountsOn(_drive).Returns(true);

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult step = StepOf(result.Value, DiagnosticStep.ShareAndCredentials);

        step.Outcome.Should().Be(DiagnosticOutcome.Warned);
        step.Finding.Should().Be(DiagnosticFinding.CredentialsUntested);
        step.Detail.Should().Be(_drive.Host);
        result.Value.HasFailure.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_NotTestAgain_WhenTheShareIsAlreadyMounted()
    {
        _nasConnectorMock.IsMountedFrom(_drive).Returns(true);
        _nasConnectorMock.IsConnected(_drive.Letter).Returns(true);

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult share = StepOf(result.Value, DiagnosticStep.ShareAndCredentials);

        share.Outcome.Should().Be(DiagnosticOutcome.Passed);
        share.Finding.Should().Be(DiagnosticFinding.AlreadyMounted);

        StepOf(result.Value, DiagnosticStep.LetterAvailable).Finding
            .Should().Be(DiagnosticFinding.LetterHeldByThisShare);

        await _nasConnectorMock.DidNotReceive().TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportRejectedCredentials_WithTheConnectorsOwnWords()
    {
        _nasConnectorMock
            .TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.FailedToConnect("Logon failure.")));

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult step = StepOf(result.Value, DiagnosticStep.ShareAndCredentials);

        step.Outcome.Should().Be(DiagnosticOutcome.Failed);
        step.Finding.Should().Be(DiagnosticFinding.CredentialsRejected);
        step.Detail.Should().Be("Logon failure.");
    }

    [Fact]
    public async Task Handle_Should_TellASessionConflictApartFromARejection()
    {
        _nasConnectorMock
            .TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(DriveErrors.SessionConflict("Already signed in.")));

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult step = StepOf(result.Value, DiagnosticStep.ShareAndCredentials);

        step.Outcome.Should().Be(DiagnosticOutcome.Failed);
        step.Finding.Should().Be(DiagnosticFinding.SessionConflict);
    }

    [Fact]
    public async Task Handle_Should_FailTheLetterCheck_WhenSomethingElseHoldsIt()
    {
        _nasConnectorMock.IsConnected(_drive.Letter).Returns(true);
        _nasConnectorMock.IsMountedFrom(_drive).Returns(false);

        Result<DriveDiagnosis> result = await _diagnoseDrive.Handle(new DiagnoseDrive.Request(_drive.Id));

        DiagnosticResult step = StepOf(result.Value, DiagnosticStep.LetterAvailable);

        step.Outcome.Should().Be(DiagnosticOutcome.Failed);
        step.Finding.Should().Be(DiagnosticFinding.LetterHeldByAnother);
        step.Detail.Should().Be(_drive.Letter);
        result.Value.HasFailure.Should().BeTrue();
    }
}
