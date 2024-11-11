using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Drives;
using Helix.Domain.Drives;
using NSubstitute;
using SharedKernel;

namespace Application.UnitTests.Drives;

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
    private readonly ICacheService _cacheServiceMock;

    public CreateDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _cacheServiceMock = Substitute.For<ICacheService>();

        _createDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _cacheServiceMock);
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

    [Fact]
    public async Task Handle_Should_CallCacheService_WhenCreateSucceeds()
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        // Act
        await _createDrive.Handle(Request);

        // Assert
        await _cacheServiceMock.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("999.999.999.999")] // Out of range
    [InlineData("256.256.256.256")] // Out of range
    [InlineData("192.168.1.1.1")]   // Too many segments
    [InlineData("192.168.1")]       // Too few segments
    [InlineData("abc.def.ghi.jkl")] // Non-numeric values
    public async Task Handle_Should_ReturnError_WhenIpAddressFormatIsInvalid(string invalidIpAddress)
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request invalidRequest = Request with { IpAddress = invalidIpAddress };

        // Act
        Result<Drive> result = await _createDrive.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(ValidationErrors.InvalidIpAddress);
    }

    [Theory]
    [InlineData("192.168.1.1")]
    [InlineData("10.0.0.1")]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("255.255.255.255")]
    [InlineData("0.0.0.0")]
    public async Task Handle_Should_ReturnSuccess_WhenIpAddressFormatIsValid(string validIpAddress)
    {
        // Arrange
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request validRequest = Request with { IpAddress = validIpAddress };

        // Act
        Result<Drive> result = await _createDrive.Handle(validRequest);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
