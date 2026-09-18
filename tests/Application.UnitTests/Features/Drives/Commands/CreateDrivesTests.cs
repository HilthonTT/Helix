using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public sealed class CreateDrivesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly CreateDrives _createDrives;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public CreateDrivesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([]);

        _driveRepositoryMock
            .IsLetterUniqueAsync(Arg.Any<string>(), UserId, Arg.Any<CancellationToken>())
            .Returns(true);

        _createDrives = new(_driveRepositoryMock, _unitOfWorkMock, _loggedInUserMock, _nasConnectorMock);
    }

    private static CreateDrives.Request Shares(params (string Letter, string Share)[] shares) => new(
        [.. shares.Select(s => new CreateDrives.NewDrive(s.Letter, "192.168.0.1", s.Share))],
        "user",
        "password",
        HomeNetworkId: "gateway:aa",
        HomeNetworkName: "Home");

    [Fact]
    public async Task Handle_Should_CreateOneDrivePerShare()
    {
        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media"), ("Y", "Backup")));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(d => (d.Letter, d.Name)).Should().Equal(("Z", "Media"), ("Y", "Backup"));
        result.Value.Should().OnlyContain(d => d.Username == "user" && d.HomeNetworkId == "gateway:aa");

        _driveRepositoryMock.Received(2).Insert(Arg.Any<Drive>());
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNothingIsChosen()
    {
        Result<List<Drive>> result = await _createDrives.Handle(Shares());

        result.Error.Should().Be(DriveErrors.NothingToCreate);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenTwoSharesAreGivenOneLetter()
    {
        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media"), ("z", "Backup")));

        result.Error.Code.Should().Be(DriveErrors.DuplicateLetter("Z").Code);
        _driveRepositoryMock.DidNotReceive().Insert(Arg.Any<Drive>());
    }

    [Fact]
    public async Task Handle_Should_SaveNothing_WhenAnyLetterIsAlreadyADrive()
    {
        _driveRepositoryMock.IsLetterUniqueAsync("Y", UserId, Arg.Any<CancellationToken>()).Returns(false);

        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media"), ("Y", "Backup")));

        result.Error.Should().Be(DriveErrors.LetterNotUnique("Y"));
        _driveRepositoryMock.DidNotReceive().Insert(Arg.Any<Drive>());
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenALetterIsHeldBySomethingElse()
    {
        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["Z"]));
        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(false);

        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media")));

        result.Error.Should().Be(DriveErrors.LetterInUse("Z"));
    }

    [Fact]
    public async Task Handle_Should_AcceptALetterAlreadyMountedFromTheSameShare()
    {
        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["Z"]));
        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(true);

        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media")));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenCredentialsAreMissing()
    {
        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media")) with { Password = "" });

        result.Error.Should().Be(ValidationErrors.MissingFields);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<Drive>> result = await _createDrives.Handle(Shares(("Z", "Media")));

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}
