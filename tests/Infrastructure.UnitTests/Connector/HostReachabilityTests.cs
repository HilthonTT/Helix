using FluentAssertions;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net.Sockets;

namespace Infrastructure.UnitTests.Connector;

/// <summary>
/// Pins down the two things this class is for: not probing one NAS once per share, and
/// never answering "unreachable" for a reason other than the host not answering.
/// </summary>
/// <remarks>
/// The second matters more than it looks. A false "unreachable" stops Helix reconnecting
/// a drive that would have come back, which is the one job the watchdog has.
/// </remarks>
public sealed class HostReachabilityTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A probe with the socket replaced by a scripted answer per port.</summary>
    private sealed class FakeReachability : HostReachability
    {
        private readonly Func<string, int, bool> _answer;

        public FakeReachability(IDateTimeProvider dateTimeProvider, Func<string, int, bool> answer)
            : base(dateTimeProvider, NullLogger<HostReachability>.Instance)
        {
            _answer = answer;
        }

        /// <summary>Ports asked about, in the order they were asked.</summary>
        public List<(string Host, int Port)> Attempts { get; } = [];

        protected override Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            Attempts.Add((host, port));

            return _answer(host, port)
                ? Task.FromResult(true)

                // What a closed port really does, so the production catch is the one under test.
                : throw new SocketException((int)SocketError.ConnectionRefused);
        }
    }

    private static IDateTimeProvider ClockAt(params DateTime[] readings)
    {
        var clock = Substitute.For<IDateTimeProvider>();

        if (readings.Length == 1)
        {
            clock.UtcNow.Returns(readings[0]);
        }
        else
        {
            clock.UtcNow.Returns(readings[0], [.. readings[1..]]);
        }

        return clock;
    }

    [Fact]
    public async Task IsReachableAsync_Should_ReportReachable_WhenSmbAnswers()
    {
        var probe = new FakeReachability(ClockAt(Now), (_, port) => port == 445);

        bool reachable = await probe.IsReachableAsync("nas.local");

        reachable.Should().BeTrue();
        probe.Attempts.Should().ContainSingle().Which.Port.Should().Be(445);
    }

    [Fact]
    public async Task IsReachableAsync_Should_FallBackToNetBios_WhenTheModernPortIsClosed()
    {
        // Older NAS firmware fronts SMB on 139 only. Writing one of those off as absent
        // would mean never reconnecting a share that works perfectly well.
        var probe = new FakeReachability(ClockAt(Now), (_, port) => port == 139);

        bool reachable = await probe.IsReachableAsync("oldnas");

        reachable.Should().BeTrue();
        probe.Attempts.Select(a => a.Port).Should().Equal(445, 139);
    }

    [Fact]
    public async Task IsReachableAsync_Should_ReportUnreachable_WhenNoPortAnswers()
    {
        var probe = new FakeReachability(ClockAt(Now), (_, _) => false);

        bool reachable = await probe.IsReachableAsync("nas.local");

        reachable.Should().BeFalse();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ProbeOnce_ForEveryShareOfOneNas()
    {
        // The caller is a loop over drives, and thirteen mapped drives are routinely
        // thirteen shares of one server. Without the cache that is thirteen handshakes
        // with the same host every sweep.
        var probe = new FakeReachability(ClockAt(Now), (_, port) => port == 445);

        bool[] answers = await Task.WhenAll(
            Enumerable.Range(0, 13).Select(_ => probe.IsReachableAsync("nas.local")));

        answers.Should().AllBeEquivalentTo(true);
        probe.Attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ProbeAgain_OnceTheReadingHasAged()
    {
        // A NAS that comes back has to be noticed, so the answer is cached for seconds,
        // not for the session.
        var probe = new FakeReachability(
            ClockAt(Now, Now.AddSeconds(30)),
            (_, port) => port == 445);

        await probe.IsReachableAsync("nas.local");
        await probe.IsReachableAsync("nas.local");

        probe.Attempts.Should().HaveCount(2);
    }

    [Fact]
    public async Task IsReachableAsync_Should_KeepHostsApart()
    {
        var probe = new FakeReachability(ClockAt(Now), (host, _) => host == "here");

        (await probe.IsReachableAsync("here")).Should().BeTrue();
        (await probe.IsReachableAsync("elsewhere")).Should().BeFalse();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ReportReachable_WhenTheProbeItselfBreaks()
    {
        // Anything that is not the host declining to answer is a fault in here, and a
        // fault in here must never be the reason a drive stops being reconnected.
        var probe = new FakeReachability(ClockAt(Now), (_, _) => throw new InvalidOperationException("broken"));

        bool reachable = await probe.IsReachableAsync("nas.local");

        reachable.Should().BeTrue();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ReportReachable_WhenThereIsNoHostToProbe()
    {
        // An empty host is a validation problem, and the connector's message about it is
        // more use than this one's.
        var probe = new FakeReachability(ClockAt(Now), (_, _) => false);

        bool reachable = await probe.IsReachableAsync("   ");

        reachable.Should().BeTrue();
        probe.Attempts.Should().BeEmpty();
    }
}
