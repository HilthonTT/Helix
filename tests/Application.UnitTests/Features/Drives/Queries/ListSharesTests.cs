using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class ListSharesTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly ListShares.Request Request = new("192.168.0.1", "user", "password");

    private readonly ListShares _listShares;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public ListSharesTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);

        _listShares = new(_driveRepositoryMock, _loggedInUserMock, _nasConnectorMock);
    }

    private void ServerShares(params string[] shares) =>
        _nasConnectorMock
            .ListSharesAsync("192.168.0.1", "user", "password", Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<string>>(shares));

    [Fact]
    public async Task Handle_Should_ListTheSharesInOrder()
    {
        ServerShares("Photos", "backup", "Media");

        Result<List<AvailableShare>> result = await _listShares.Handle(Request);

        result.Value.Select(s => s.Name).Should().Equal("backup", "Media", "Photos");
        result.Value.Should().OnlyContain(s => !s.AlreadyAdded);
    }

    [Fact]
    public async Task Handle_Should_MarkSharesThatAreAlreadyDrives()
    {
        ServerShares("Media", "Backup");

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns(
        [
            Drive.Create(UserId, "M", "192.168.0.1", "media", "user", "password"),
            Drive.Create(UserId, "B", "192.168.0.2", "Backup", "user", "password"),
        ]);

        Result<List<AvailableShare>> result = await _listShares.Handle(Request);

        result.Value.Single(s => s.Name == "Media").ExistingLetter.Should().Be("M");
        result.Value.Single(s => s.Name == "Backup").AlreadyAdded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_PassOnWhyTheServerCouldNotBeListed()
    {
        Error refused = DriveErrors.SharesNotListed("Access denied.");

        _nasConnectorMock
            .ListSharesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<IReadOnlyList<string>>(refused));

        Result<List<AvailableShare>> result = await _listShares.Handle(Request);

        result.Error.Should().Be(refused);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenTheHostIsInvalid()
    {
        Result<List<AvailableShare>> result = await _listShares.Handle(Request with { Host = "not a host" });

        result.Error.Should().Be(ValidationErrors.InvalidHost);
        await _nasConnectorMock.DidNotReceive().ListSharesAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenCredentialsAreMissing()
    {
        Result<List<AvailableShare>> result = await _listShares.Handle(Request with { Password = " " });

        result.Error.Should().Be(ValidationErrors.MissingFields);
    }
}
