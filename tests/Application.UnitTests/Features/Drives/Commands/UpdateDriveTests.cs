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
        // Arrange — the drive is mounted at L and is being moved to Z. Its own instance:
        // Update mutates the drive, and the shared one is passed around by other tests.
        Drive drive = Drive.Create(UserId, "L", "192.168.0.1", "Name", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId).Returns(drive);
        _driveRepositoryMock.IsLetterUniqueAsync(Request.Letter, UserId).Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["L"]));
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>()).Returns(Result.Success());

        // Act
        Result result = await _updateDrive.Handle(Request);

        // Assert — the mapping nothing will own any more is cancelled, quietly.
        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.Received(1).DisconnectAsync(drive);
        _driveMonitorMock.Received(1).Suppress(Arg.Is<IEnumerable<string>>(letters => letters.Contains("L")));
    }

    [Fact]
    public async Task Handle_Should_NotUnmount_WhenTheLetterIsUnchanged()
    {
        // Arrange
        Drive drive = Drive.Create(UserId, "L", "192.168.0.1", "Name", "Username", "Password");

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId).Returns(drive);
        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["L"]));

        // Act
        Result result = await _updateDrive.Handle(Request with { Letter = "l" });

        // Assert
        result.IsSuccess.Should().BeTrue();
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(Arg.Any<Drive>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotASingleCharacter()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request invalidRequest = Request with { Letter = "LE" };

        // Act
        Result result = await _updateDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotUnique()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(false);

        UpdateDrive.Request invalidRequest = Request with { Letter = "A" };

        // Act
        Result result = await _updateDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(DriveErrors.LetterNotUnique(invalidRequest.Letter));
    }

    [Theory]
    [InlineData("")]
    [InlineData("999.999.999.999")]  // Dotted-numeric, but out of range
    [InlineData("256.256.256.256")]  // Dotted-numeric, but out of range
    [InlineData("192.168.1.1.1")]    // Dotted-numeric with too many segments
    [InlineData("192.168.1")]        // Dotted-numeric with too few segments
    [InlineData("nas local")]        // A space is not legal in a hostname
    [InlineData("-nas")]             // A label may not start with a hyphen
    [InlineData("nas-")]             // ...nor end with one
    [InlineData("nas..local")]       // Empty label
    [InlineData("fd00:::5")]         // Not a parseable IPv6 address
    public async Task Handle_Should_ReturnError_WhenHostFormatIsInvalid(string invalidHost)
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request invalidRequest = Request with { Host = invalidHost };

        // Act
        Result result = await _updateDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(ValidationErrors.InvalidHost);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("nas.local")]        // The usual way a NAS is reached on a home network
    [InlineData("MYNAS")]            // Single-label NetBIOS name
    [InlineData("nas_01.example.com")] // Underscores are legal in a Windows computer name
    [InlineData("abc.def.ghi.jkl")]  // Not an IP address, but a perfectly good hostname
    [InlineData("fd00::5")]          // IPv6
    [InlineData("[fd00::5]")]        // IPv6 in the bracketed form other tools print
    public async Task Handle_Should_ReturnSuccess_WhenHostFormatIsValid(string validHost)
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request validRequest = Request with { Host = validHost };

        // Act
        Result result = await _updateDrive.Handle(validRequest);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
