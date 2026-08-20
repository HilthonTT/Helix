using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Features.Drives.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Queries;

public sealed class GetAvailableDriveLettersTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly GetAvailableDriveLetters _getAvailableDriveLetters;

    private readonly IDriveRepository _driveRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public GetAvailableDriveLettersTests()
    {
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _nasConnectorMock.GetConnectedLetters().Returns([]);
        HaveDrives();

        _getAvailableDriveLetters = new(_driveRepositoryMock, _loggedInUserMock, _nasConnectorMock);
    }

    private void HaveDrives(params Drive[] drives) =>
        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([.. drives]);

    private void Mounted(params string[] letters) =>
        _nasConnectorMock.GetConnectedLetters()
            .Returns(new HashSet<string>(letters, StringComparer.OrdinalIgnoreCase));

    private static Drive DriveOn(string letter) =>
        Drive.Create(UserId, letter, "nas.local", $"Share {letter}", "user", "password");

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<List<string>> result = await _getAvailableDriveLetters.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_OfferTheWholeAlphabet_WhenNothingIsTaken()
    {
        Result<List<string>> result = await _getAvailableDriveLetters.Handle();

        result.Value.Should().HaveCount(26).And.StartWith("A").And.EndWith("Z");
    }

    /// <summary>
    /// The gap the old uniqueness check left: a letter belonging to a USB stick, an
    /// optical drive or another account's mapping is not in this user's drive list, and
    /// used to be offered right up until the connect failed.
    /// </summary>
    [Fact]
    public async Task Handle_Should_ExcludeLettersTheOperatingSystemAlreadyHas()
    {
        Mounted("C", "D");

        Result<List<string>> result = await _getAvailableDriveLetters.Handle();

        result.Value.Should().NotContain("C").And.NotContain("D").And.Contain("E");
    }

    [Fact]
    public async Task Handle_Should_ExcludeLettersTakenByThisUsersOwnDrives()
    {
        HaveDrives(DriveOn("Z"), DriveOn("Y"));

        Result<List<string>> result = await _getAvailableDriveLetters.Handle();

        result.Value.Should().NotContain("Z").And.NotContain("Y").And.Contain("X");
    }

    [Fact]
    public async Task Handle_Should_KeepTheEditedDrivesOwnLetter()
    {
        Drive edited = DriveOn("Z");
        HaveDrives(edited, DriveOn("Y"));

        // Its own letter is in use by itself; excluding it would force a letter change
        // on anyone editing an unrelated field.
        Result<List<string>> result = await _getAvailableDriveLetters.Handle(
            new GetAvailableDriveLetters.Request(edited.Id));

        result.Value.Should().Contain("Z").And.NotContain("Y");
    }

    [Fact]
    public async Task Handle_Should_KeepTheEditedDrivesLetter_EvenWhileItIsMounted()
    {
        Drive edited = DriveOn("Z");
        HaveDrives(edited);
        Mounted("C", "Z");

        Result<List<string>> result = await _getAvailableDriveLetters.Handle(
            new GetAvailableDriveLetters.Request(edited.Id));

        result.Value.Should().Contain("Z").And.NotContain("C");
    }

    [Fact]
    public async Task Handle_Should_MatchLettersRegardlessOfCase()
    {
        Mounted("c");

        Result<List<string>> result = await _getAvailableDriveLetters.Handle();

        result.Value.Should().NotContain("C");
    }
}
