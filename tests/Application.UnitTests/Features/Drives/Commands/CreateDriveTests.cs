using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class CreateDriveTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly CreateDrive.Request Request = new(
        "Z",
        "192.168.0.1",
        "Name",
        "Username",
        "Password");

    private readonly CreateDrive _createDrive;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;

    public CreateDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _createDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotASingleCharacter()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request invalidRequest = Request with { Letter = "LE" };

        // Act
        Result<Drive> result = await _createDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotUnique()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(false);

        CreateDrive.Request invalidRequest = Request with { Letter = "A" };

        // Act
        Result<Drive> result = await _createDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(DriveErrors.LetterNotUnique(invalidRequest.Letter));
    }

    [Fact]
    public async Task Handle_Should_CallRepository_WhenCreateSucceeds()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        // Act
        await _createDrive.Handle(Request);

        // Assert
        _driveRepositoryMock.Received(1).Insert(Arg.Is<Drive>(d => d.Letter == Request.Letter));
    }

    [Fact]
    public async Task Handle_Should_CallUnitOfWork_WhenCreateSucceeds()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        // Act
        await _createDrive.Handle(Request);

        // Assert
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request invalidRequest = Request with { Host = invalidHost };

        // Act
        Result<Drive> result = await _createDrive.Handle(invalidRequest);

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

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request validRequest = Request with { Host = validHost };

        // Act
        Result<Drive> result = await _createDrive.Handle(validRequest);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
