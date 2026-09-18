using FluentAssertions;
using Helix.Infrastructure.Time;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.UnitTests.Time;

public sealed class CountdownServiceTests
{
    [Fact]
    public void ATickThatArrivesAfterStop_Should_ChangeNothing()
    {
        using var countdown = new CountdownService(NullLogger<CountdownService>.Instance);

        bool finished = false;
        countdown.CountdownFinished += (_, _) => finished = true;

        countdown.Start(1);
        countdown.Stop();

        countdown.OnCountdownTick(null, null);

        countdown.SecondsRemaining.Should().Be(1);
        finished.Should().BeFalse();
    }

    [Fact]
    public void ATickWhileRunning_Should_CountDownAndFinishAtZero()
    {
        using var countdown = new CountdownService(NullLogger<CountdownService>.Instance);

        bool finished = false;
        countdown.CountdownFinished += (_, _) => finished = true;

        countdown.Start(1);

        countdown.OnCountdownTick(null, null);

        countdown.SecondsRemaining.Should().Be(0);
        countdown.IsRunning.Should().BeFalse();
        finished.Should().BeTrue();
    }
}
