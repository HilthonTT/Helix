using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class GetUnmanagedMappingsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly GetUnmanagedMappings _getUnmanagedMappings;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public GetUnmanagedMappingsTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _getUnmanagedMappings = new(_driveRepositoryMock, _loggedInUserMock, _nasConnectorMock);
    }

    [Fact]
    public async Task Handle_Should_LeaveOutLettersHelixAlreadyManages()
    {
        _nasConnectorMock.GetMappedShares().Returns(
        [
            new MappedShare("Z", "nas", "Media"),
            new MappedShare("P", "nas", "Photos"),
            new MappedShare("M", "nas", "Music"),
        ]);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns(
        [
            Drive.Create(UserId, "z", "nas", "Media", "user", "password"),
        ]);

        Result<List<MappedShare>> result = await _getUnmanagedMappings.Handle();

        result.Value.Select(m => m.Letter).Should().Equal("M", "P");
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotLoggedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<MappedShare>> result = await _getUnmanagedMappings.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }
}
