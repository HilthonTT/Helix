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
    private readonly INasConnector _nasConnectorMock;

    public CreateDriveTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _nasConnectorMock = Substitute.For<INasConnector>();
        _nasConnectorMock.GetConnectedLetters().Returns([]);

        _createDrive = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _nasConnectorMock);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotASingleCharacter()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request invalidRequest = Request with { Letter = "LE" };

        Result<Drive> result = await _createDrive.Handle(invalidRequest);

        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenLetterIsNotUnique()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(false);

        CreateDrive.Request invalidRequest = Request with { Letter = "A" };

        Result<Drive> result = await _createDrive.Handle(invalidRequest);

        result.Error.Should().Be(DriveErrors.LetterNotUnique(invalidRequest.Letter));
    }

    [Fact]
    public async Task Handle_Should_CallRepository_WhenCreateSucceeds()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        await _createDrive.Handle(Request);

        _driveRepositoryMock.Received(1).Insert(Arg.Is<Drive>(d => d.Letter == Request.Letter));
    }

    [Fact]
    public async Task Handle_Should_CallUnitOfWork_WhenCreateSucceeds()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        await _createDrive.Handle(Request);

        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
    [InlineData("192168110")]
    public async Task Handle_Should_ReturnError_WhenHostFormatIsInvalid(string invalidHost)
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request invalidRequest = Request with { Host = invalidHost };

        Result<Drive> result = await _createDrive.Handle(invalidRequest);

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

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        CreateDrive.Request validRequest = Request with { Host = validHost };

        Result<Drive> result = await _createDrive.Handle(validRequest);

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("É")]
    [InlineData("1")]
    [InlineData("ß")]
    public async Task Handle_Should_ReturnNotALetter_WhenLetterIsNotAToZ(string letter)
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        Result<Drive> result = await _createDrive.Handle(Request with { Letter = letter });

        result.Error.Should().Be(DriveErrors.NotALetter);
    }

    [Fact]
    public async Task Handle_Should_ReturnLetterInUse_WhenLetterIsMountedFromSomethingElse()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([Request.Letter]);
        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(false);

        Result<Drive> result = await _createDrive.Handle(Request);

        result.Error.Should().Be(DriveErrors.LetterInUse(Request.Letter));
        _driveRepositoryMock.DidNotReceive().Insert(Arg.Any<Drive>());
    }

    [Fact]
    public async Task Handle_Should_ReturnSuccess_WhenLetterIsAlreadyMountedFromThisShare()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Arg.Is<string>(e => e == Request.Letter), _loggedInUserMock.UserId)
            .Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([Request.Letter]);
        _nasConnectorMock.IsMountedFrom(Arg.Is<Drive>(d => d.Letter == Request.Letter && d.Name == Request.Name))
            .Returns(true);

        Result<Drive> result = await _createDrive.Handle(Request);

        result.IsSuccess.Should().BeTrue();
        _driveRepositoryMock.Received(1).Insert(Arg.Any<Drive>());
    }

    [Fact]
    public async Task Handle_Should_TrimTheShareAndUsername()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.IsLetterUniqueAsync(Request.Letter, UserId).Returns(true);

        Result<Drive> result = await _createDrive.Handle(Request with { Name = " photos ", Username = "bob " });

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("photos");
        result.Value.Username.Should().Be("bob");
    }
}
