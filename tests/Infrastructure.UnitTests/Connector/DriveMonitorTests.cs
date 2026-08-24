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
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty("a drive that stayed connected has not changed state");
    }

    [Fact]
    public async Task Poll_Should_Report_A_Drive_That_Dropped()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — the share goes away between polls.
        Connected();
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new DriveConnectivityChange(MediaId, "M", false));
    }

    [Fact]
    public async Task Poll_Should_Report_A_Drive_That_Came_Back()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        Connected();
        await monitor.PollAsync();

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        Connected("M");
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle()
            .Which.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Watch_Should_Seed_From_Reality_So_An_Offline_Drive_Is_Not_A_Fresh_Drop()
    {
        // Arrange — the drive is already offline when it starts being watched. Treating
        // that as a drop would make the watchdog try to reconnect a drive the user has
        // never connected, and would write a bogus "lost its connection" audit entry.
        DriveMonitor monitor = CreateMonitor();
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Poll_Should_Only_Report_Drives_That_Are_Watched()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — an unwatched letter disappears.
        Connected("M");
        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty("only the watched set is reported on");
    }

    [Fact]
    public async Task Poll_Should_Report_Each_Change_Once()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — the drive stays down across several polls.
        Connected();
        await monitor.PollAsync();
        await monitor.PollAsync();
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle("a drop is an edge, not a level");
    }

    [Fact]
    public async Task Poll_Should_Batch_Simultaneous_Changes()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M"), new WatchedDrive(BackupId, "N")]);

        var batches = new List<IReadOnlyList<DriveConnectivityChange>>();
        monitor.ConnectivityChanged += (_, batch) => batches.Add(batch);

        // Act — the NAS goes down, taking both shares with it.
        Connected();
        await monitor.PollAsync();

        // Assert
        batches.Should().ContainSingle("one poll raises one event");
        batches[0].Should().HaveCount(2);
        batches[0].Select(c => c.DriveId).Should().BeEquivalentTo([MediaId, BackupId]);
    }

    [Fact]
    public async Task Watch_Should_Be_Case_Insensitive_About_Letters()
    {
        // Arrange — letters are stored as the user typed them.
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "m")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        Connected();
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle()
            .Which.Letter.Should().Be("m", "the drive's own letter is reported back, not a normalised one");
    }

    [Fact]
    public async Task Watch_Should_Replace_The_Previous_Set()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);
        monitor.Watch([new WatchedDrive(BackupId, "N")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — both go down, but only N is watched now.
        Connected();
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle()
            .Which.DriveId.Should().Be(BackupId);
    }

    /// <summary>
    /// The reported bug: disconnect every drive and be told, seconds later, that they
    /// reconnected. The monitor saw thirteen letters vanish at once, reported it as a
    /// NAS falling over, and the watchdog put them all back.
    /// </summary>
    [Fact]
    public async Task Suppress_Should_Swallow_A_Disconnect_The_App_Made_Itself()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — the letter goes away while Helix is the one unmounting it.
        using (monitor.Suppress(["M"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty("the app asked for this, so nothing has changed as far as anyone else is concerned");
    }

    [Fact]
    public async Task Suppress_Should_Swallow_A_Connect_The_App_Made_Itself()
    {
        // Arrange — nothing mounted, as it is when the dashboard first opens.
        DriveMonitor monitor = CreateMonitor();
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        using (monitor.Suppress(["M"]))
        {
            Connected("M");
            await monitor.PollAsync();
        }

        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty("the startup connect is not news the tray needs to break");
    }

    /// <summary>
    /// The suppression covers the operation, not just the polls inside it: releasing
    /// re-seeds the baseline, so the very next poll has nothing to report either.
    /// </summary>
    [Fact]
    public async Task Suppress_Should_Leave_The_New_State_As_The_Baseline()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — no poll at all while suppressed, which is the usual case.
        using (monitor.Suppress(["M"]))
        {
            Connected();
        }

        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppress_Should_Report_Again_Once_It_Is_Released()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        using (monitor.Suppress(["M"]))
        {
            Connected();
        }

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — the user brings it back by hand in Explorer.
        Connected("M");
        await monitor.PollAsync();

        // Assert
        changes.Should().ContainSingle().Which.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task Suppress_Should_Only_Cover_The_Letters_It_Names()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M", "N");
        monitor.Watch([new WatchedDrive(MediaId, "M"), new WatchedDrive(BackupId, "N")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act — Helix unmounts M; the NAS drops N at the same moment.
        using (monitor.Suppress(["M"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        // Assert
        changes.Should().ContainSingle("N dropped on its own and still needs chasing")
            .Which.DriveId.Should().Be(BackupId);
    }

    [Fact]
    public async Task Suppress_Should_Be_Case_Insensitive_About_Letters()
    {
        // Arrange — letters are stored as the user typed them.
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "m")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        using (monitor.Suppress(["m"]))
        {
            Connected();
            await monitor.PollAsync();
        }

        // Assert
        changes.Should().BeEmpty();
    }

    /// <summary>
    /// Two operations may name the same drive — a group being taken down while the row's
    /// own disconnect button is in flight. The first to finish must not uncover it.
    /// </summary>
    [Fact]
    public async Task Suppress_Should_Nest()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        List<DriveConnectivityChange> changes = Capture(monitor);

        // Act
        IDisposable outer = monitor.Suppress(["M"]);
        IDisposable inner = monitor.Suppress(["M"]);

        inner.Dispose();

        Connected();
        await monitor.PollAsync();

        changes.Should().BeEmpty("the outer suppression is still holding the letter");

        outer.Dispose();

        await monitor.PollAsync();

        // Assert
        changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Suppress_Should_Tolerate_Being_Disposed_Twice()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        IDisposable first = monitor.Suppress(["M"]);
        IDisposable second = monitor.Suppress(["M"]);

        // Act — a double dispose must not release the count the other caller holds.
        first.Dispose();
        first.Dispose();

        // Assert
        List<DriveConnectivityChange> changes = Capture(monitor);

        Connected();
        await monitor.PollAsync();

        changes.Should().BeEmpty();

        second.Dispose();
    }

    [Fact]
    public void Stop_Should_Be_Safe_To_Call_Without_Starting()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor();

        // Act
        Action stop = monitor.Stop;

        // Assert
        stop.Should().NotThrow();
        monitor.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task Start_Should_Poll_On_The_Interval()
    {
        // Arrange
        DriveMonitor monitor = CreateMonitor("M");
        monitor.Watch([new WatchedDrive(MediaId, "M")]);

        var seen = new TaskCompletionSource<DriveConnectivityChange>();
        monitor.ConnectivityChanged += (_, batch) => seen.TrySetResult(batch[0]);

        // Act
        Connected();
        monitor.Start(TimeSpan.FromMilliseconds(50));

        // Assert
        DriveConnectivityChange change = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        change.IsConnected.Should().BeFalse();

        monitor.IsRunning.Should().BeTrue();

        monitor.Stop();
    }
}
