using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class MarkDriveConnectedTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 9, 9, 8, 14, 0, DateTimeKind.Utc);

    private readonly MarkDriveConnected _markDriveConnected;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;

    public MarkDriveConnectedTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _dateTimeProviderMock.UtcNow.Returns(Now);

        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(true);

        _markDriveConnected = new(
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _dateTimeProviderMock);
    }

    private static Drive Media { get; } =
        Drive.Create(UserId, "Z", "nas.local", "Media", "user", "password");

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    [Fact]
    public async Task Handle_Should_StampTheDrive_WhenItIsMounted()
    {
        HaveDrives(Media);

        Result<Drive> result = await _markDriveConnected.Handle(new MarkDriveConnected.Request("z"));

        result.IsSuccess.Should().BeTrue();
        result.Value.LastConnectedOnUtc.Should().Be(Now);

        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheLetterIsNotADrive()
    {
        HaveDrives(Media);

        Result<Drive> result = await _markDriveConnected.Handle(new MarkDriveConnected.Request("Y"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriveErrors.LetterNotFound("Y"));

        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheMountIsNotThere()
    {
        HaveDrives(Media);

        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(false);

        Result<Drive> result = await _markDriveConnected.Handle(new MarkDriveConnected.Request("Z"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriveErrors.MountNotAvailable);

        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenTheLetterIsNotALetter()
    {
        Result<Drive> result = await _markDriveConnected.Handle(new MarkDriveConnected.Request("É"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_Fail_WhenNobodyIsSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<Drive> result = await _markDriveConnected.Handle(new MarkDriveConnected.Request("Z"));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}
