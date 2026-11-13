using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class DiscoverServersTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly DiscoverServers _discoverServers;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasDiscovery _nasDiscoveryMock;

    public DiscoverServersTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasDiscoveryMock = Substitute.For<INasDiscovery>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);

        _discoverServers = new(_driveRepositoryMock, _loggedInUserMock, _nasDiscoveryMock);
    }

    private void Found(params DiscoveredHost[] hosts) =>
        _nasDiscoveryMock.DiscoverAsync(Arg.Any<CancellationToken>()).Returns(hosts);

    private void Drives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([.. drives]);

    [Fact]
    public async Task Handle_Should_NameEachServerByTheBestNameItHas()
    {
        Found(
            new DiscoveredHost("192.0.2.10", "Studio", "ds920.local", "DS920+"),
            new DiscoveredHost("192.0.2.11", null, "qnap.local", null),
            new DiscoveredHost("192.0.2.12", null, null, null));

        Result<List<DiscoveredServer>> result = await _discoverServers.Handle();

        result.Value.Select(s => s.DisplayName).Should().Equal("192.0.2.12", "qnap", "Studio");
        result.Value.Single(s => s.DisplayName == "Studio").Model.Should().Be("DS920+");
    }

    [Fact]
    public async Task Handle_Should_ListEachAddressOnce()
    {
        Found(
            new DiscoveredHost("192.0.2.10", "NAS", null, null),
            new DiscoveredHost("192.0.2.10", "NAS", null, null));

        Result<List<DiscoveredServer>> result = await _discoverServers.Handle();

        result.Value.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_Should_MarkServersThatAlreadyHaveDrives_UnderAnySpelling()
    {
        Found(
            new DiscoveredHost("192.0.2.10", "Studio", "ds920.local", null),
            new DiscoveredHost("192.0.2.11", "BACKUP", null, null),
            new DiscoveredHost("192.0.2.12", "Other", null, null));

        Drives(
            Drive.Create(UserId, "M", "192.0.2.10", "media", "user", "password"),
            Drive.Create(UserId, "P", "DS920", "photos", "user", "password"),
            Drive.Create(UserId, "B", "backup.lan", "backup", "user", "password"));

        Result<List<DiscoveredServer>> result = await _discoverServers.Handle();

        result.Value.Single(s => s.Address == "192.0.2.10").ExistingLetters.Should().Equal("M", "P");
        result.Value.Single(s => s.Address == "192.0.2.11").ExistingLetters.Should().Equal("B");
        result.Value.Single(s => s.Address == "192.0.2.12").AlreadyAdded.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_ReturnAnEmptyList_WhenNothingAnswers()
    {
        Found();

        Result<List<DiscoveredServer>> result = await _discoverServers.Handle();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<DiscoveredServer>> result = await _discoverServers.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
        await _nasDiscoveryMock.DidNotReceive().DiscoverAsync(Arg.Any<CancellationToken>());
    }
}
