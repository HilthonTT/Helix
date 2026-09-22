using FluentAssertions;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Infrastructure.UnitTests.Connector;

public sealed class WakeOnLanTests
{
    private const string Mac = "1a-2b-3c-4d-5e-6f";

    private static readonly DateTime Now = new(2026, 9, 21, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FakeWakeOnLan : WakeOnLan
    {
        private readonly bool _answer;

        public FakeWakeOnLan(IDateTimeProvider dateTimeProvider, bool answer = true)
            : base(dateTimeProvider, NullLogger<WakeOnLan>.Instance)
        {
            _answer = answer;
        }

        public List<byte[]> Sent { get; } = [];

        protected override bool Transmit(byte[] packet)
        {
            Sent.Add(packet);

            return _answer;
        }
    }

    private static IDateTimeProvider ClockAt(params DateTime[] readings)
    {
        var clock = Substitute.For<IDateTimeProvider>();

        clock.UtcNow.Returns(readings[0], readings[1..]);

        return clock;
    }

    [Fact]
    public void BuildPacket_Should_BeSixFFsThenTheAddressSixteenTimes()
    {
        byte[] hardware = [0x1a, 0x2b, 0x3c, 0x4d, 0x5e, 0x6f];

        byte[] packet = WakeOnLan.BuildPacket(hardware);

        packet.Should().HaveCount(102);
        packet.Take(6).Should().OnlyContain(b => b == 0xFF);

        for (int repeat = 0; repeat < 16; repeat++)
        {
            packet.Skip(6 + (repeat * 6)).Take(6).Should().Equal(hardware);
        }
    }

    [Fact]
    public async Task TryWakeAsync_Should_SendOnce_ThenStayQuiet()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddSeconds(30)));

        (await wake.TryWakeAsync(Mac)).Should().BeTrue();
        (await wake.TryWakeAsync(Mac)).Should().BeFalse();

        wake.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryWakeAsync_Should_SendAgain_OnceTheQuietPeriodIsOver()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddMinutes(6)));

        await wake.TryWakeAsync(Mac);
        await wake.TryWakeAsync(Mac);

        wake.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task TryWakeAsync_Should_TreatSpellingsOfOneAddressAsOne()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddSeconds(30)));

        await wake.TryWakeAsync("1A:2B:3C:4D:5E:6F");
        await wake.TryWakeAsync(Mac);

        wake.Sent.Should().HaveCount(1);
    }

    [Fact]
    public async Task TryWakeAsync_Should_KeepEachAddressOnItsOwnQuietPeriod()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddSeconds(30)));

        await wake.TryWakeAsync(Mac);
        await wake.TryWakeAsync("aa-bb-cc-dd-ee-ff");

        wake.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task WakeNowAsync_Should_IgnoreTheQuietPeriod()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddSeconds(5)));

        await wake.TryWakeAsync(Mac);
        (await wake.WakeNowAsync(Mac)).Should().BeTrue();

        wake.Sent.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nas.local")]
    public async Task TryWakeAsync_Should_SendNothing_ForSomethingThatIsNotAnAddress(string? macAddress)
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now));

        (await wake.TryWakeAsync(macAddress)).Should().BeFalse();

        wake.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TryWakeAsync_Should_ReportIt_WhenNothingCouldBeSent()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now), answer: false);

        (await wake.TryWakeAsync(Mac)).Should().BeFalse();
    }

    [Fact]
    public async Task TryWakeAsync_Should_NotStayQuiet_AfterAPacketThatNeverWentOut()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now.AddSeconds(30)), answer: false);

        await wake.TryWakeAsync(Mac);
        await wake.TryWakeAsync(Mac);

        wake.Sent.Should().HaveCount(2);
    }

    [Fact]
    public async Task LookUpAsync_Should_AnswerNothing_ForAnEmptyHost()
    {
        var wake = new FakeWakeOnLan(ClockAt(Now, Now));

        (await wake.LookUpAsync("  ")).Should().BeNull();
    }
}
