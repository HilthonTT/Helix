using FluentAssertions;
using Helix.Application.Abstractions.Storage;
using Helix.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.UnitTests.Storage;

public sealed class StorageProbeTests
{
    private const long Terabyte = 1024L * 1024 * 1024 * 1024;

    private sealed class FakeProbe(
        Dictionary<string, (long Total, long Free)> capacities,
        params string[] vanishing)
        : StorageProbe(NullLogger.Instance)
    {
        protected override string RootPathFor(string letter) => letter;

        protected override (long TotalBytes, long FreeBytes)? ReadCapacity(string rootPath)
        {
            if (vanishing.Contains(rootPath))
            {
                throw new DriveNotFoundException($"Could not find the drive '{rootPath}'.");
            }

            return capacities.TryGetValue(rootPath, out (long Total, long Free) reading)
                ? (reading.Total, reading.Free)
                : null;
        }
    }

    [Fact]
    public async Task ProbeAsync_Should_ReturnNothing_WhenNoDrivesAreGiven()
    {
        IReadOnlyList<VolumeUsage> volumes = await new FakeProbe([]).ProbeAsync([]);

        volumes.Should().BeEmpty();
    }

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

        volumes.Sum(v => v.UsedBytes).Should().Be(total - freeReadings.Min());
    }

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

    [Fact]
    public async Task ProbeAsync_Should_NameTheDrivesEachVolumeIsMountedAs()
    {
        var probe = new FakeProbe(new()
        {
            ["Y"] = (4 * Terabyte, Terabyte),
            ["Z"] = (4 * Terabyte, Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle().Which.Letters.Should().Equal("Y", "Z");
    }

    [Fact]
    public async Task ProbeAsync_Should_LeaveOutTheLettersItCouldNotMeasure()
    {
        var probe = new FakeProbe(new()
        {
            ["Z"] = (4 * Terabyte, Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle().Which.Letters.Should().Equal("Z");
    }

    [Fact]
    public async Task ProbeAsync_Should_ReportFreeSpaceAsAShareOfTheVolume()
    {
        var probe = new FakeProbe(new()
        {
            ["Z"] = (4 * Terabyte, Terabyte),
        });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Z"]);

        volumes[0].FreeBytes.Should().Be(Terabyte);
        volumes[0].FreePercent.Should().Be(25);
    }

    [Fact]
    public async Task ProbeAsync_Should_LeaveOutDrivesThatAreNotReachable()
    {
        var probe = new FakeProbe(new() { ["Z"] = (20 * Terabyte, 5 * Terabyte) });

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Y", "Z"]);

        volumes.Should().ContainSingle();
        volumes.Sum(v => v.TotalBytes).Should().Be(20 * Terabyte);
    }

    [Fact]
    public async Task ProbeAsync_Should_LeaveOutADriveThatIsUnmappedWhileItIsBeingMeasured()
    {
        var probe = new FakeProbe(
            new() { ["Z"] = (20 * Terabyte, 5 * Terabyte) },
            "Q");

        IReadOnlyList<VolumeUsage> volumes = await probe.ProbeAsync(["Q", "Z"]);

        volumes.Should().ContainSingle();
        volumes.Sum(v => v.TotalBytes).Should().Be(20 * Terabyte);
        volumes[0].Letters.Should().Equal("Z");
    }
}
