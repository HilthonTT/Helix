using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class UpdateDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly UpdateDrive.Request Request = new(
        Guid.NewGuid(),
        "Z",
        "192.168.0.1",
        "Name",
        "Username",
        "Password");

    private static readonly Drive DummyDrive = Drive.Create(
        UserId,
        "L",
        "192.168.0.1",
        "Name",
        "Username",
        "Password");

    private readonly UpdateDrive _updateDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;

    public UpdateDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _nasConnectorMock = Substitute.For<INasConnector>();
        _nasConnectorMock.GetConnectedLetters().Returns([]);

        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _driveMonitorMock.Suppress(Arg.Any<IEnumerable<string>>()).Returns(Substitute.For<IDisposable>());

        _updateDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _nasConnectorMock, _driveMonitorMock);
    }

    [Fact]
    public async Task Handle_Should_UnmountTheOldLetter_WhenTheLetterChangesWhileMounted()
    {
        Drive drive = Drive.Create(UserId, "L", "192.168.0.1", "Name", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId).Returns(drive);
        _driveRepositoryMock.IsLetterUniqueAsync(Request.Letter, UserId).Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["L"]));
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>()).Returns(Result.Success());

        Result result = await _updateDrive.Handle(Request);

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.Received(1).DisconnectAsync(drive);
        _driveMonitorMock.Received(1).Suppress(Arg.Is<IEnumerable<string>>(letters => letters.Contains("L")));
    }

    [Fact]
    public async Task Handle_Should_NotUnmount_WhenTheLetterIsUnchanged()
    {
        Drive drive = Drive.Create(UserId, "L", "192.168.0.1", "Name", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId).Returns(drive);
        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["L"]));

        Result result = await _updateDrive.Handle(Request with { Letter = "l" });

        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotASingleCharacter()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request invalidRequest = Request with { Letter = "LE" };

        Result result = await _updateDrive.Handle(invalidRequest);

        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotUnique()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(false);

        UpdateDrive.Request invalidRequest = Request with { Letter = "A" };

        Result result = await _updateDrive.Handle(invalidRequest);

        result.Error.Should().Be(DriveErrors.LetterNotUnique(invalidRequest.Letter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("999.999.999.999")]
    [InlineData("256.256.256.256")]
    [InlineData("192.168.1.1.1")]
    [InlineData("192.168.1")]
    [InlineData("nas local")]
    [InlineData("-nas")]
    [InlineData("nas-")]
    [InlineData("nas..local")]
    [InlineData("fd00:::5")]
    public async Task Handle_Should_ReturnError_WhenHostFormatIsInvalid(string invalidHost)
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request invalidRequest = Request with { Host = invalidHost };

        Result result = await _updateDrive.Handle(invalidRequest);

        result.Error.Should().Be(ValidationErrors.InvalidHost);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("nas.local")]
    [InlineData("MYNAS")]
    [InlineData("nas_01.example.com")]
    [InlineData("abc.def.ghi.jkl")]
    [InlineData("fd00::5")]
    [InlineData("[fd00::5]")]
    public async Task Handle_Should_ReturnSuccess_WhenHostFormatIsValid(string validHost)
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request validRequest = Request with { Host = validHost };

        Result result = await _updateDrive.Handle(validRequest);

        result.IsSuccess.Should().BeTrue();
    }
}
