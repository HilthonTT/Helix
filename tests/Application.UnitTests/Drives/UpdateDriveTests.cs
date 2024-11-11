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
    private readonly ICacheService _cacheServiceMock;

    public UpdateDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _cacheServiceMock = Substitute.For<ICacheService>();

        _updateDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _cacheServiceMock);
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

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request invalidRequest = Request with { IpAddress = invalidIpAddress };

        // Act
        Result result = await _updateDrive.Handle(invalidRequest);

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

        _driveRepositoryMock.GetByIdAsync(Request.DriveId)
            .Returns(DummyDrive);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        UpdateDrive.Request validRequest = Request with { IpAddress = validIpAddress };

        // Act
        Result result = await _updateDrive.Handle(validRequest);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}
