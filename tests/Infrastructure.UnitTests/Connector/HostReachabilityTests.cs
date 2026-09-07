using FluentAssertions;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Net.Sockets;

namespace Infrastructure.UnitTests.Connector;

public sealed class HostReachabilityTests
{
    private static readonly DateTime Now = new(2026, 8, 23, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FakeReachability : HostReachability
    {
        private readonly Func<string, int, bool> _answer;

        public FakeReachability(IDateTimeProvider dateTimeProvider, Func<string, int, bool> answer)
            : base(dateTimeProvider, NullLogger<HostReachability>.Instance)
        {
            _answer = answer;
        }

        public List<(string Host, int Port)> Attempts { get; } = [];

        protected override Task<bool> CanConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            Attempts.Add((host, port));

            return _answer(host, port)
                ? Task.FromResult(true)

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
        var probe = new FakeReachability(ClockAt(Now), (_, port) => port == 445);

        bool[] answers = await Task.WhenAll(
            Enumerable.Range(0, 13).Select(_ => probe.IsReachableAsync("nas.local")));

        answers.Should().AllBeEquivalentTo(true);
        probe.Attempts.Should().ContainSingle();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ProbeAgain_OnceTheReadingHasAged()
    {
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
        var probe = new FakeReachability(ClockAt(Now), (_, _) => throw new InvalidOperationException("broken"));

        bool reachable = await probe.IsReachableAsync("nas.local");

        reachable.Should().BeTrue();
    }

    [Fact]
    public async Task IsReachableAsync_Should_ReportReachable_WhenThereIsNoHostToProbe()
    {
        var probe = new FakeReachability(ClockAt(Now), (_, _) => false);

        bool reachable = await probe.IsReachableAsync("   ");

        reachable.Should().BeTrue();
        probe.Attempts.Should().BeEmpty();
    }
}
