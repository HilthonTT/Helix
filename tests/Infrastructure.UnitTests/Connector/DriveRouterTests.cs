using FluentAssertions;
using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Infrastructure.UnitTests.Connector;

public sealed class DriveRouterTests
{
    private const string Home = "192.168.1.6";
    private const string Away = "nas.tailnet.ts.net";

    private readonly IHostReachability _reachability = Substitute.For<IHostReachability>();
    private readonly INetworkLocation _networkLocation = Substitute.For<INetworkLocation>();
    private readonly IWakeOnLan _wakeOnLan = Substitute.For<IWakeOnLan>();

    private readonly DriveRouter _router;

    private readonly Drive _drive = Drive.Create(Guid.NewGuid(), "Z", Home, "Media", "user", "password");

    public DriveRouterTests()
    {
        _networkLocation.IsSupported.Returns(true);

        _drive.RememberMacAddress("1a-2b-3c-4d-5e-6f");

        _router = new DriveRouter(_reachability, _networkLocation, _wakeOnLan, NullLogger<DriveRouter>.Instance);
    }

    private void Answers(string host, bool reachable)
    {
        _reachability.IsReachableAsync(host, Arg.Any<CancellationToken>()).Returns(reachable);
        _reachability.ProbeNowAsync(host, Arg.Any<CancellationToken>()).Returns(reachable);
    }

    private void PinnedAndOn(string networkId)
    {
        _drive.PinToNetwork("gateway:home", "Home");

        _networkLocation.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new NetworkLocation(networkId, "Network"));
    }

    [Fact]
    public async Task RouteAsync_Should_UseHome_WhenHomeAnswers()
    {
        _drive.ReachAwayAt(Away);
        Answers(Home, true);

        Answers(Away, true);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Value.Should().Be(new DriveRoute(Home, IsRemote: false));
    }

    [Fact]
    public async Task RouteAsync_Should_AskAfterBothAddressesAtOnce_WhenItMayNeedEither()
    {
        _drive.ReachAwayAt(Away);

        var home = new TaskCompletionSource<bool>();
        _reachability.IsReachableAsync(Home, Arg.Any<CancellationToken>()).Returns(home.Task);
        Answers(Away, true);

        Task<Result<DriveRoute>> routing = _router.RouteAsync(_drive);

        await _reachability.Received(1).IsReachableAsync(Away, Arg.Any<CancellationToken>());

        home.SetResult(false);

        (await routing).Value.Should().Be(new DriveRoute(Away, IsRemote: true));
    }

    [Fact]
    public async Task RouteAsync_Should_FallBackToAway_WhenHomeIsSilent()
    {
        _drive.ReachAwayAt(Away);
        Answers(Home, false);
        Answers(Away, true);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Value.Should().Be(new DriveRoute(Away, IsRemote: true));
    }

    [Fact]
    public async Task RouteAsync_Should_GoStraightToAway_WhenPinnedAndAwayFromHome()
    {
        _drive.ReachAwayAt(Away);
        PinnedAndOn("gateway:cafe");
        Answers(Away, true);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Value.Should().Be(new DriveRoute(Away, IsRemote: true));
        await _reachability.DidNotReceive().IsReachableAsync(Home, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_Should_TryHomeFirst_WhenPinnedAndOnTheHomeNetwork()
    {
        _drive.ReachAwayAt(Away);
        PinnedAndOn("gateway:home");
        Answers(Home, true);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Value.IsRemote.Should().BeFalse();
    }

    [Fact]
    public async Task RouteAsync_Should_SayHomeIsUnreachable_WhenThereIsNoAddressForAway()
    {
        Answers(Home, false);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Error.Should().Be(DriveErrors.HostUnreachable(Home));
    }

    [Fact]
    public async Task RouteAsync_Should_NameTheAwayAddress_WhenAwayAndItIsSilentToo()
    {
        _drive.ReachAwayAt(Away);
        PinnedAndOn("gateway:cafe");
        Answers(Away, false);

        Result<DriveRoute> route = await _router.RouteAsync(_drive);

        route.Error.Should().Be(DriveErrors.HostUnreachable(Away));
    }

    [Fact]
    public async Task RouteAsync_Should_WakeTheNas_WhenHomeIsSilent()
    {
        Answers(Home, false);

        await _router.RouteAsync(_drive);

        await _wakeOnLan.Received(1).TryWakeAsync("1a-2b-3c-4d-5e-6f", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_Should_NotWakeTheNas_WhenHomeAnswers()
    {
        Answers(Home, true);

        await _router.RouteAsync(_drive);

        await _wakeOnLan.DidNotReceive().TryWakeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RouteAsync_Should_NotWakeTheNas_WhenAwayFromItsHomeNetwork(bool hasAddressForAway)
    {
        if (hasAddressForAway)
        {
            _drive.ReachAwayAt(Away);
        }

        PinnedAndOn("gateway:cafe");
        Answers(Home, false);
        Answers(Away, false);

        await _router.RouteAsync(_drive);

        await _wakeOnLan.DidNotReceive().TryWakeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RouteAsync_Should_ProbeAfresh_WhenAskedTo()
    {
        Answers(Home, true);

        await _router.RouteAsync(_drive, fresh: true);

        await _reachability.Received(1).ProbeNowAsync(Home, Arg.Any<CancellationToken>());
        await _reachability.DidNotReceive().IsReachableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
