using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
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
}
