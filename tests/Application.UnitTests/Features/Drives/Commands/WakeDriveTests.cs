using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class WakeDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly WakeDrive _wakeDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly IWakeOnLan _wakeOnLanMock;

    private readonly Drive _drive;

    public WakeDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _wakeOnLanMock = Substitute.For<IWakeOnLan>();

        _wakeDrive = new(_driveRepositoryMock, _loggedInUserMock, _wakeOnLanMock);

        _drive = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");
        _drive.RememberMacAddress("1a-2b-3c-4d-5e-6f");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(_drive.Id, Arg.Any<CancellationToken>()).Returns(_drive);

        _wakeOnLanMock.WakeNowAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    [Fact]
    public async Task Handle_Should_SendTheWakeUp_IgnoringTheQuietPeriod()
    {
        Result result = await _wakeDrive.Handle(new WakeDrive.Request(_drive.Id));

        result.IsSuccess.Should().BeTrue();
        await _wakeOnLanMock.Received(1).WakeNowAsync("1a-2b-3c-4d-5e-6f", Arg.Any<CancellationToken>());
        await _wakeOnLanMock.DidNotReceive().TryWakeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Refuse_WhenTheDriveHasNoHardwareAddress()
    {
        _drive.RememberMacAddress(null);

        Result result = await _wakeDrive.Handle(new WakeDrive.Request(_drive.Id));

        result.Error.Should().Be(DriveErrors.NoMacAddress);
        await _wakeOnLanMock.DidNotReceive().WakeNowAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportIt_WhenNothingCouldBeSent()
    {
        _wakeOnLanMock.WakeNowAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(false);

        Result result = await _wakeDrive.Handle(new WakeDrive.Request(_drive.Id));

        result.Error.Should().Be(DriveErrors.WakeNotSent);
    }

    [Fact]
    public async Task Handle_Should_Refuse_AnotherUsersDrive()
    {
        _loggedInUserMock.UserId.Returns(Guid.NewGuid());

        Result result = await _wakeDrive.Handle(new WakeDrive.Request(_drive.Id));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnNotFound_WhenTheDriveIsGone()
    {
        Guid missing = Guid.NewGuid();

        Result result = await _wakeDrive.Handle(new WakeDrive.Request(missing));

        result.Error.Should().Be(DriveErrors.NotFound(missing));
    }

    [Fact]
    public async Task Handle_Should_Refuse_WhenSignedOut()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result result = await _wakeDrive.Handle(new WakeDrive.Request(_drive.Id));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}
