using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Storage;
using NSubstitute;

namespace Application.UnitTests.Features.Storage.Queries;

public sealed class GetStorageHistoryTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private const long Tb = 1_000_000_000_000;

    private const long Gb = 1_000_000_000;

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private readonly IStorageSampleRepository _sampleRepositoryMock = Substitute.For<IStorageSampleRepository>();
    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IDateTimeProvider _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

    private readonly GetStorageHistory _handler;

    public GetStorageHistoryTests()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);

        _driveRepositoryMock
            .GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(
            [
                Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password"),
                Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "user", "password"),
            ]);

        _handler = new(_sampleRepositoryMock, _driveRepositoryMock, _loggedInUserMock, _dateTimeProviderMock);
    }

    private static StorageSample Sample(int daysAgo, long freeBytes, string volumeId = "capacity:43", string letters = "Y,Z") =>
        StorageSample.Create(UserId, volumeId, Today.AddDays(-daysAgo), 43 * Tb, freeBytes, letters, Now.AddDays(-daysAgo));

    private void GivenSamples(int days, params StorageSample[] samples) =>
        _sampleRepositoryMock
            .GetSinceAsync(UserId, Today.AddDays(-days), Arg.Any<CancellationToken>())
            .Returns([.. samples]);

    [Fact]
    public async Task Handle_Should_ReturnEachVolumesReadings_OldestFirst()
    {
        GivenSamples(
            90,
            Sample(1, 10 * Tb),
            Sample(20, 11 * Tb),
            Sample(10, 10 * Tb + 500 * Gb));

        Result<List<VolumeHistory>> result = await _handler.Handle(new GetStorageHistory.Request(90));

        VolumeHistory volume = result.Value.Single();
        volume.Points.Select(p => p.Day).Should().BeInAscendingOrder();
        volume.Points.Should().HaveCount(3);
        volume.FreeBytes.Should().Be(10 * Tb);
        volume.DriveNames.Should().Equal("Y: Backups", "Z: Media");
    }

    [Fact]
    public async Task Handle_Should_ForecastFromTheLastMonth_EvenOverALongerRange()
    {
        GivenSamples(
            365,
            Sample(300, 1 * Tb),
            Sample(20, 12 * Tb),
            Sample(10, 11 * Tb),
            Sample(1, 10 * Tb));

        Result<List<VolumeHistory>> result = await _handler.Handle(new GetStorageHistory.Request(365));

        VolumeHistory volume = result.Value.Single();
        volume.BytesPerDay.Should().BeNegative();
        volume.DaysUntilFull.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Should_ClampTheRange()
    {
        GivenSamples(GetStorageHistory.MaximumDays);

        await _handler.Handle(new GetStorageHistory.Request(10_000));

        await _sampleRepositoryMock.Received(1).GetSinceAsync(
            UserId,
            Today.AddDays(-GetStorageHistory.MaximumDays),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_PutTheVolumeFillingUpSoonestFirst()
    {
        GivenSamples(
            90,
            Sample(20, 5 * Tb, volumeId: "steady", letters: "X"),
            Sample(10, 5 * Tb, volumeId: "steady", letters: "X"),
            Sample(1, 5 * Tb, volumeId: "steady", letters: "X"),
            Sample(20, 3 * Tb, volumeId: "filling", letters: "Z"),
            Sample(10, 2 * Tb, volumeId: "filling", letters: "Z"),
            Sample(1, 1 * Tb, volumeId: "filling", letters: "Z"));

        Result<List<VolumeHistory>> result = await _handler.Handle(new GetStorageHistory.Request(90));

        result.Value.Select(v => v.VolumeId).Should().Equal("filling", "steady");
    }
}
