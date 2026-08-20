using FluentAssertions;
using Helix.Application.Abstractions.Storage;
using Helix.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.UnitTests.Storage;

/// <summary>
/// Pins down the deduplication, which is the whole reason the probe exists.
/// </summary>
/// <remarks>
/// Adding up per-drive capacity is wrong whenever a NAS is mapped more than once: every
/// share of one pool reports that pool's entire size, so a 43 TB server mapped nine times
/// read as nearly 400 TB on the dashboard.
/// </remarks>
public sealed class StorageProbeTests
{
    private const long Terabyte = 1024L * 1024 * 1024 * 1024;

    /// <summary>A probe with the filesystem replaced by a table of readings.</summary>
    private sealed class FakeProbe(Dictionary<string, (long Total, long Free)> capacities)
        : StorageProbe(NullLogger.Instance)
    {
        protected override string RootPathFor(string letter) => letter;

        protected override (long TotalBytes, long FreeBytes)? ReadCapacity(string rootPath) =>
            capacities.TryGetValue(rootPath, out (long Total, long Free) reading)
                ? (reading.Total, reading.Free)
                : null;
    }

    [Fact]
    public async Task ProbeAsync_Should_ReturnNothing_WhenNoDrivesAreGiven()
    {
        IReadOnlyList<VolumeUsage> volumes = await new FakeProbe([]).ProbeAsync([]);

        volumes.Should().BeEmpty();
    }

    /// <summary>
    /// The reported bug, with the reporter's own readings taken off a live QNAP: thirteen
    /// shares of one 43.2 TB pool, whose free space drifted about 12 MB across the probe
    /// pass because the NAS was recording to one of them at the time.
    /// </summary>
    /// <remarks>
    /// Not one pair of these free-space values is equal, which is exactly why an earlier
    /// attempt that keyed on total-and-free merged nothing and still totalled 400 TB.
    /// </remarks>
    [Fact]
    public async Task ProbeAsync_Should_CountOnePoolOnce_WhenItsFreeSpaceDriftsBetweenReadings()
    {
        const long total = 47531060183040;

        long[] freeReadings =
        [
            11965351247872, 11965347778560, 11965347024896, 11965346172928, 11965345796096,
            11965344993280, 11965343612928, 11965342908416, 11965340844032, 11965340024832,
            11965339353088, 11965338583040, 11965338304512,
        ];

        string[] letters = ["A", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z"];

        var probe = new FakeProbe(letters
            .Select((letter, index) => (letter, free: freeReadings[index]))
            .ToDictionary(entry => entry.letter, entry => (total, entry.free)));

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(letters);

        volumes.Should().ContainSingle();
        volumes.Sum(v => v.TotalBytes).Should().Be(total);

        // The smallest free reading is kept, so the figure is the same however the
        // parallel probes happen to finish.
        volumes.Sum(v => v.UsedBytes).Should().Be(total - freeReadings.Min());
    }

    /// <summary>
    /// The case the volume-serial approach was meant to catch and got wrong anyway: a
    /// server reporting a different serial, and a different label, for every share.
    /// Identity comes from the capacity, so nothing the server names can split these.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_Should_Merge_EvenWhenTheServerNamesEveryShareDifferently()
    {
        var probe = new FakeProbe(new()
        {
            ["Y"] = (43 * Terabyte, 10 * Terabyte),
            ["Z"] = (43 * Terabyte, 10 * Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle();
    }

    [Fact]
    public async Task ProbeAsync_Should_CountSeparateVolumesSeparately()
    {
        var probe = new FakeProbe(new()
        {
            ["Y"] = (10 * Terabyte, 4 * Terabyte),
            ["Z"] = (20 * Terabyte, 5 * Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().HaveCount(2);
        volumes.Sum(v => v.TotalBytes).Should().Be(30 * Terabyte);
    }

    /// <summary>
    /// The known cost of keying on size alone, pinned down so it is a decision rather
    /// than a surprise: two volumes of byte-identical size are counted once, even when
    /// their contents differ. Real volume sizes are not round numbers, so an exact
    /// collision means two identically built volumes — and the result understates the
    /// total rather than multiplying it.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_Should_MergeByteIdenticalVolumes_EvenWhenTheirContentsDiffer()
    {
        var probe = new FakeProbe(new()
        {
            ["Y"] = (20 * Terabyte, 4 * Terabyte),
            ["Z"] = (20 * Terabyte, 9 * Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle();
        volumes.Sum(v => v.TotalBytes).Should().Be(20 * Terabyte);
    }

    /// <summary>
    /// Whichever probe finishes first must not change the number on screen, or the tile
    /// flickers between refreshes as the readings drift.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_Should_ReportTheSameFigure_WhateverOrderTheDrivesAreGivenIn()
    {
        var probe = new FakeProbe(new()
        {
            ["X"] = (20 * Terabyte, 9 * Terabyte),
            ["Y"] = (20 * Terabyte, 4 * Terabyte),
            ["Z"] = (20 * Terabyte, 7 * Terabyte),
        });

        IReadOnlyList<VolumeUsage> forwards = await probe.ProbeAsync(["X", "Y", "Z"]);
        IReadOnlyList<VolumeUsage> backwards = await probe.ProbeAsync(["Z", "Y", "X"]);

        forwards.Sum(v => v.UsedBytes).Should().Be(backwards.Sum(v => v.UsedBytes));
        forwards.Sum(v => v.UsedBytes).Should().Be(16 * Terabyte);
    }

    /// <summary>
    /// Shares with separate quotas on one pool report separate totals, so they are counted
    /// separately — which is the right answer for what has been allotted to them.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_Should_CountQuotaLimitedSharesSeparately()
    {
        var probe = new FakeProbe(new()
        {
            ["Y"] = (10 * Terabyte, 3 * Terabyte),
            ["Z"] = (20 * Terabyte, 5 * Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Sum(v => v.TotalBytes).Should().Be(30 * Terabyte);
        volumes.Sum(v => v.UsedBytes).Should().Be(22 * Terabyte);
    }

    /// <summary>
    /// An unreachable share keeps its capacity — it is simply unknown right now. Counting
    /// it as zero would make the dashboard total shrink every time a drive dropped.
    /// </summary>
    [Fact]
    public async Task ProbeAsync_Should_LeaveOutDrivesThatAreNotReachable()
    {
        var probe = new FakeProbe(new() { ["Z"] = (20 * Terabyte, 5 * Terabyte) });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle();
        volumes.Sum(v => v.TotalBytes).Should().Be(20 * Terabyte);
    }
}
