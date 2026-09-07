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
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result result = await _testDriveConnection.Handle(Request);

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenHostIsInvalid()
    {
        SignIn();

        TestDriveConnection.Request invalidRequest = Request with { Host = "999.999.999.999" };

        Result result = await _testDriveConnection.Handle(invalidRequest);

        result.Error.Should().Be(ValidationErrors.InvalidHost);
    }

    [Fact]
    public async Task Handle_Should_NotReachTheNetwork_WhenTheFormIsIncomplete()
    {
        SignIn();

        TestDriveConnection.Request invalidRequest = Request with { Password = "  " };

        Result result = await _testDriveConnection.Handle(invalidRequest);

        result.Error.Should().Be(ValidationErrors.MissingFields);

        await _nasConnectorMock.DidNotReceive().TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_TestTheDetailsOnTheForm()
    {
        SignIn();

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        Result result = await _testDriveConnection.Handle(Request);

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

    [Fact]
    public async Task Handle_Should_NeverConnectOrMountTheCandidate()
    {
        SignIn();

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());

        await _testDriveConnection.Handle(Request);

        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_SurfaceTheConnectorsFailure()
    {
        SignIn();

        Error expected = DriveErrors.FailedToConnect("The password is incorrect.");

        _nasConnectorMock.TestAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(expected));

        Result result = await _testDriveConnection.Handle(Request);

        result.Error.Should().Be(expected);
    }
}
