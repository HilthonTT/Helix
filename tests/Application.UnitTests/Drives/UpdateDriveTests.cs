using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
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
        Guid.NewGuid(),
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
}
