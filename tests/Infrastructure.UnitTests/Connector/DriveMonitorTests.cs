using FluentAssertions;
using Helix.Application.Abstractions.Connector;
using Helix.Infrastructure.Connector;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Infrastructure.UnitTests.Connector;

public sealed class DriveMonitorTests
{
    private static readonly Guid MediaId = Guid.NewGuid();
    private static readonly Guid BackupId = Guid.NewGuid();

    private readonly INasConnector _nasConnector = Substitute.For<INasConnector>();

    private DriveMonitor CreateMonitor(params string[] connected)
    {
        Connected(connected);

        return new DriveMonitor(_nasConnector, NullLogger<DriveMonitor>.Instance);
    }

    private void Connected(params string[] letters)
    {
        _nasConnector.GetConnectedLetters()
            .Returns(_ => new HashSet<string>(letters, StringComparer.OrdinalIgnoreCase));
    }

    private static List<DriveConnectivityChange> Capture(DriveMonitor monitor)
    {
        List<DriveConnectivityChange> changes = [];

        monitor.ConnectivityChanged += (_, batch) => changes.AddRange(batch);

        return changes;
    }

    [Fact]
    public async Task Poll_Should_Report_Nothing_When_State_Is_Unchanged()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        await monitor.PollAsync();

        changes.Should().BeEmpty("a drive that stayed connected has not changed state");
    }

    [Fact]
    public async Task Poll_Should_Report_A_Drive_That_Dropped()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();

        changes.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DriveConnectivityChange(MediaId, "M", false));
    }

    [Fact]
    public async Task Poll_Should_Report_A_Drive_That_Came_Back()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        Connected();
        await monitor.PollAsync();

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected("M");
        await monitor.PollAsync();

        changes.Should().ContainSingle()
            .Which.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Watch_Should_Seed_From_Reality_So_An_Offline_Drive_Is_Not_A_Fresh_Drop()
    {
        DriveMonitor monitor = CreateMonitor();
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        await monitor.PollAsync();

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Poll_Should_Only_Report_Drives_That_Are_Watched()
    {
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected("M");
        await monitor.PollAsync();

        changes.Should().BeEmpty("only the watched set is reported on");
    }

    [Fact]
    public async Task Poll_Should_Report_Each_Change_Once()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();
        await monitor.PollAsync();
        await monitor.PollAsync();

        changes.Should().ContainSingle("a drop is an edge, not a level");
    }

    [Fact]
    public async Task Poll_Should_Batch_Simultaneous_Changes()
    {
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M"), new WatchedDrive(BackupId, "N")]);

        var batches = new List<IReadOnlyList<DriveConnectivityChange>>();
        monitor.ConnectivityChanged += (_, batch) => batches.Add(batch);

        Connected();
        await monitor.PollAsync();

        batches.Should().ContainSingle("one poll raises one event");
        batches[0].Should().HaveCount(2);
        batches[0].Select(c => c.DriveId).Should().BeEquivalentTo([MediaId, BackupId]);
    }

    [Fact]
    public async Task Watch_Should_Be_Case_Insensitive_About_Letters()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "m")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();

        changes.Should().ContainSingle()
            .Which.Letter.Should().Be("m", "the drive's own letter is reported back, not a normalised one");
    }

    [Fact]
    public async Task Watch_Should_Replace_The_Previous_Set()
    {
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);
        monitor.Watch([new WatchedDrive(BackupId, "N")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();

        changes.Should().ContainSingle()
            .Which.DriveId.Should().Be(BackupId);
    }

    [Fact]
    public async Task Suppress_Should_Swallow_A_Disconnect_The_App_Made_Itself()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        using (monitor.Suppress(["M"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        await monitor.PollAsync();

        changes.Should().BeEmpty("the app asked for this, so nothing has changed as far as anyone else is concerned");
    }

    [Fact]
    public async Task Suppress_Should_Swallow_A_Connect_The_App_Made_Itself()
    {
        DriveMonitor monitor = CreateMonitor();
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        using (monitor.Suppress(["M"]))
        {
            Connected("M");
            await monitor.PollAsync();
        }

        await monitor.PollAsync();

        changes.Should().BeEmpty("the startup connect is not news the tray needs to break");
    }

    [Fact]
    public async Task Suppress_Should_Leave_The_New_State_As_The_Baseline()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        using (monitor.Suppress(["M"]))
        {
            Connected();
        }

        await monitor.PollAsync();

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppress_Should_Report_Again_Once_It_Is_Released()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        using (monitor.Suppress(["M"]))
        {
            Connected();
        }

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected("M");
        await monitor.PollAsync();

        changes.Should().ContainSingle().Which.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Suppress_Should_Not_Report_A_Drop_When_Released_During_A_Poll()
    {
        DriveMonitor monitor = CreateMonitor();
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        IDisposable suppression = monitor.Suppress(["M"]);

        bool released = false;
        _nasConnector.GetConnectedLetters().Returns(_ =>
        {
            if (!released)
            {
                released = true;
                Connected("M");
                suppression.Dispose();

                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            return new HashSet<string>(["M"], StringComparer.OrdinalIgnoreCase);
        });

        await monitor.PollAsync();
        await monitor.PollAsync();

        changes.Should().BeEmpty("the letter was connected by Helix on purpose, not lost");
    }

    [Fact]
    public async Task Suppress_Should_Only_Cover_The_Letters_It_Names()
    {
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M"), new WatchedDrive(BackupId, "N")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        using (monitor.Suppress(["M"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        changes.Should().ContainSingle("N dropped on its own and still needs chasing")
            .Which.DriveId.Should().Be(BackupId);
    }

    [Fact]
    public async Task Suppress_Should_Be_Case_Insensitive_About_Letters()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "m")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        using (monitor.Suppress(["m"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppress_Should_Nest()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        IDisposable outer = monitor.Suppress(["M"]);
        IDisposable inner = monitor.Suppress(["M"]);

        inner.Dispose();

        Connected();
        await monitor.PollAsync();

        changes.Should().BeEmpty("the outer suppression is still holding the letter");

        outer.Dispose();

        await monitor.PollAsync();

        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppress_Should_Tolerate_Being_Disposed_Twice()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        IDisposable first = monitor.Suppress(["M"]);
        IDisposable second = monitor.Suppress(["M"]);

        first.Dispose();
        first.Dispose();

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();

        changes.Should().BeEmpty();

        second.Dispose();
    }

    [Fact]
    public void Stop_Should_Be_Safe_To_Call_Without_Starting()
    {
        DriveMonitor monitor = CreateMonitor();

        Action stop = monitor.Stop;

        stop.Should().NotThrow();
        monitor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Start_Should_Poll_On_The_Interval()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        var seen = new TaskCompletionSource<DriveConnectivityChange>();
        monitor.ConnectivityChanged += (_, batch) => seen.TrySetResult(batch[0]);

        Connected();
        monitor.Start(TimeSpan.FromMilliseconds(50));

        DriveConnectivityChange change = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        change.IsConnected.Should().BeFalse();

        monitor.IsRunning.Should().BeTrue();

        monitor.Stop();
    }


    [Fact]
    public async Task Watch_Should_Keep_The_Baseline_Of_A_Drive_It_Already_Watches()
    {
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        monitor.Watch([new WatchedDrive(MediaId, "M"), new WatchedDrive(BackupId, "N")]);

        await monitor.PollAsync();

        changes.Should().ContainSingle("a drop between two polls must not be hidden by a re-watch")
            .Which.DriveId.Should().Be(MediaId);
    }
}
