using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Drives.Commands;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Drives.Commands;

public class TestDriveConnectionTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly TestDriveConnection.Request Request = new(
        "Z",
        "nas.local",
        "Name",
        "Username",
        "Password");

    private readonly TestDriveConnection _testDriveConnection;

    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;

    public TestDriveConnectionTests()
    {
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();

        _testDriveConnection = new(_loggedInUserMock, _nasConnectorMock);
    }

    private void SignIn()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotSignedIn()
    {
        // Arrange
        _loggedInUserMock.IsLoggedIn.Returns(false);

        // Act
        Result result = await _testDriveConnection.Handle(Request);

        // Assert
        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenHostIsInvalid()
    {
        // Arrange
        SignIn();

        TestDriveConnection.Request invalidRequest = Request with { Host = "999.999.999.999" };

        // Act
        Result result = await _testDriveConnection.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(ValidationErrors.InvalidHost);
    }

    [Fact]
    public async Task Handle_Should_NotReachTheNetwork_WhenTheFormIsIncomplete()
    {
        // Arrange
        SignIn();

        TestDriveConnection.Request invalidRequest = Request with { Password = "  " };

        // Act
        Result result = await _testDriveConnection.Handle(invalidRequest);

        // Assert
        result.Error.Should().Be(ValidationErrors.MissingFields);

        await _nasConnectorMock.DidNotReceive().TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_TestTheDetailsOnTheForm()
    {
        // Arrange
        SignIn();

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        Result result = await _testDriveConnection.Handle(Request);

        // Assert
        result.IsSuccess.Should().BeTrue();

        await _nasConnectorMock.Received(1).TestAsync(
            Arg.Is<Drive>(d =>
                d.Letter == "Z" &&
                d.Host == Request.Host &&
                d.Name == Request.Name &&
                d.Username == Request.Username &&
                d.Password == Request.Password),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The candidate exists only to carry the form's values to the connector. Saving it
    /// would create a drive the user never asked for, out of a form they may yet cancel.
    /// </summary>
    [Fact]
    public async Task Handle_Should_NeverConnectOrMountTheCandidate()
    {
        // Arrange
        SignIn();

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        // Act
        await _testDriveConnection.Handle(Request);

        // Assert
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SurfaceTheConnectorsFailure()
    {
        // Arrange
        SignIn();

        Error expected = DriveErrors.FailedToConnect("The password is incorrect.");

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(expected));

        // Act
        Result result = await _testDriveConnection.Handle(Request);

        // Assert
        result.Error.Should().Be(expected);
    }
}
