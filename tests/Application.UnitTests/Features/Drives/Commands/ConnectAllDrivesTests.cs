using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class ConnectAllDrivesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ConnectAllDrives _connectAllDrives;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    public ConnectAllDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        _connectAllDrives = new(
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock,
            _dateTimeProviderMock);
    }

    private static Drive Automatic { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password", autoConnect: true);

    private static Drive Manual { get; } =
        Drive.Create(UserId, "Y", "nas.local", "Backup", "user", "password", autoConnect: false);

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    [Fact]
    public async Task Handle_Should_ConnectEveryDrive_WhenTheUserAskedForIt()
    {
        HaveDrives(Automatic, Manual);

        Result result = await _connectAllDrives.Handle();

        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SkipDrivesHeldBack_WhenThePassIsUnattended()
    {
        HaveDrives(Automatic, Manual);

        Result result = await _connectAllDrives.Handle(new ConnectAllDrives.Request(OnlyAutoConnect: true));

        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_DoNothing_WhenNoDriveOptsIntoTheUnattendedPass()
    {
        HaveDrives(Manual);

        Result result = await _connectAllDrives.Handle(new ConnectAllDrives.Request(OnlyAutoConnect: true));

        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_TellTheMonitorTheMountsAreItsOwn()
    {
        HaveDrives(Automatic, Manual);

        await _connectAllDrives.Handle();

        _driveMonitorMock.Received(1).Suppress(
            Arg.Is<IEnumerable<string>>(letters => letters.OrderBy(l => l).SequenceEqual(new[] { "Y", "Z" })));
    }

    [Fact]
    public async Task Handle_Should_ReleaseTheSuppressionWhenItIsDone()
    {
        HaveDrives(Automatic);

        var suppression = Substitute.For<IDisposable>();
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(suppression);

        await _connectAllDrives.Handle();

        suppression.Received(1).Dispose();
    }

    [Fact]
    public async Task Handle_Should_SkipDrivesThatAreAlreadyMounted()
    {
        HaveDrives(Automatic, Manual);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == "Z")).Returns(true);

        await _connectAllDrives.Handle();

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Automatic, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(Manual, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_StillTryADrive_WhoseLetterIsHeldBySomethingElse()
    {
        HaveDrives(Automatic);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z"]);
        _nasConnectorMock.IsMountedFrom(Automatic).Returns(false);

        await _connectAllDrives.Handle();

        await _nasConnectorMock.Received(1).ConnectAsync(Automatic, Arg.Any<CancellationToken>());
    }
}
