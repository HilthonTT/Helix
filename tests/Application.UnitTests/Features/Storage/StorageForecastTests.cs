using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Core.Storage;
using Helix.Application.Features.Storage.Commands;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Helix.Domain.Drives;
using Helix.Domain.Storage;
using NSubstitute;

namespace Application.UnitTests.Features.Storage;

public sealed class StorageForecastTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private const long Tb = 1_000_000_000_000;

    private const long Gb = 1_000_000_000;

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    private readonly IStorageSampleRepository _sampleRepositoryMock = Substitute.For<IStorageSampleRepository>();
    private readonly IDriveRepository _driveRepositoryMock = Substitute.For<IDriveRepository>();
    private readonly IUnitOfWork _unitOfWorkMock = Substitute.For<IUnitOfWork>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly INasConnector _nasConnectorMock = Substitute.For<INasConnector>();
    private readonly IStorageProbe _storageProbeMock = Substitute.For<IStorageProbe>();
    private readonly IDateTimeProvider _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

    private readonly Drive _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media", "user", "password");
    private readonly Drive _backups = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "user", "password");

    public StorageForecastTests()
    {
        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);

        _driveRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([_media, _backups]);

        _nasConnectorMock.GetConnectedLetters().Returns(new HashSet<string>(["Z", "Y"]));
        _nasConnectorMock.IsLiveFrom(Arg.Any<Drive>()).Returns(true);

        _sampleRepositoryMock.GetForDayAsync(UserId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private RecordStorageSamples Recorder() => new(
        _sampleRepositoryMock,
        _driveRepositoryMock,
        _unitOfWorkMock,
        _loggedInUserMock,
        _nasConnectorMock,
        _storageProbeMock,
        _dateTimeProviderMock);

    private GetStorageForecasts Forecaster() => new(
        _sampleRepositoryMock,
        _driveRepositoryMock,
        _loggedInUserMock,
        _dateTimeProviderMock);

    private static StorageSample Sample(int daysAgo, long freeBytes, string volumeId = "capacity:43", string letters = "Y,Z") =>
        StorageSample.Create(UserId, volumeId, Today.AddDays(-daysAgo), 43 * Tb, freeBytes, letters, Now.AddDays(-daysAgo));

    [Fact]
    public void BytesPerDay_Should_FollowASteadyDecline()
    {
        double? rate = StorageTrend.BytesPerDay(
        [
            (Today.AddDays(-10), 10 * Tb),
            (Today.AddDays(-5), 10 * Tb - 50 * Gb),
            (Today, 10 * Tb - 100 * Gb),
        ]);

        rate.Should().BeApproximately(-10 * Gb, 1);
    }

    [Fact]
    public void BytesPerDay_Should_RefuseToGuess_FromLessThanAWeek()
    {
        StorageTrend.BytesPerDay(
        [
            (Today.AddDays(-3), 10 * Tb),
            (Today.AddDays(-2), 9 * Tb),
            (Today, 8 * Tb),
        ]).Should().BeNull();
    }

    [Theory]
    [InlineData(1_000, -10.0, 100)]
    [InlineData(1_000, 0.0, null)]
    [InlineData(1_000, 5.0, null)]
    [InlineData(1_000_000, -0.1, null)]
    public void DaysUntilFull_Should_OnlyAnswerForAVolumeThatIsFillingUp(long free, double rate, int? expected)
    {
        StorageTrend.DaysUntilFull(free, rate).Should().Be(expected);
    }

    [Fact]
    public async Task Record_Should_StoreOneSamplePerVolume()
    {
        _storageProbeMock
            .ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new VolumeUsage("capacity:43", 43 * Tb, 30 * Tb, ["Z", "Y"])]);

        StorageSample? inserted = null;
        _sampleRepositoryMock.Insert(Arg.Do<StorageSample>(s => inserted = s));

        Result<int> result = await Recorder().Handle();

        result.Value.Should().Be(1);
        inserted!.VolumeId.Should().Be("capacity:43");
        inserted.Day.Should().Be(Today);
        inserted.FreeBytes.Should().Be(13 * Tb);
        inserted.Letters.Should().Be("Y,Z");

        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await _sampleRepositoryMock.Received(1).RemoveOlderThanAsync(
            UserId,
            Today.AddDays(-RecordStorageSamples.RetentionDays),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Record_Should_UpdateTodaysSample_RatherThanAddASecond()
    {
        StorageSample earlier = Sample(0, 14 * Tb);
        _sampleRepositoryMock.GetForDayAsync(UserId, Today, Arg.Any<CancellationToken>()).Returns([earlier]);

        _storageProbeMock
            .ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new VolumeUsage("capacity:43", 43 * Tb, 30 * Tb, ["Z", "Y"])]);

        await Recorder().Handle();

        earlier.FreeBytes.Should().Be(13 * Tb);
        _sampleRepositoryMock.DidNotReceive().Insert(Arg.Any<StorageSample>());
    }

    [Fact]
    public async Task Record_Should_RecordNothing_WhenNoDriveIsUp()
    {
        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.IsLiveFrom(Arg.Any<Drive>()).Returns(false);

        Result<int> result = await Recorder().Handle();

        result.Value.Should().Be(0);
        await _storageProbeMock.DidNotReceive().ProbeAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Forecast_Should_SayWhenAFillingVolumeRunsOut()
    {
        _sampleRepositoryMock
            .GetSinceAsync(UserId, Today.AddDays(-GetStorageForecasts.WindowDays), Arg.Any<CancellationToken>())
            .Returns([Sample(20, 1 * Tb + 200 * Gb), Sample(10, 1 * Tb + 100 * Gb), Sample(0, 1 * Tb)]);

        Result<List<StorageForecast>> result = await Forecaster().Handle();

        StorageForecast forecast = result.Value.Should().ContainSingle().Subject;
        forecast.DriveNames.Should().Equal("Backups", "Media");
        forecast.FreeBytes.Should().Be(1 * Tb);
        forecast.DaysUntilFull.Should().Be(100);
    }

    [Fact]
    public async Task Forecast_Should_LeaveOutAVolumeNotMeasuredLately()
    {
        _sampleRepositoryMock
            .GetSinceAsync(UserId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([Sample(25, 2 * Tb), Sample(15, 1 * Tb + 500 * Gb), Sample(GetStorageForecasts.StaleAfterDays + 1, 1 * Tb)]);

        Result<List<StorageForecast>> result = await Forecaster().Handle();

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Forecast_Should_PutTheSoonestFirst()
    {
        _sampleRepositoryMock
            .GetSinceAsync(UserId, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                Sample(20, 5 * Tb, "capacity:1", "Y"), Sample(10, 5 * Tb, "capacity:1", "Y"), Sample(0, 5 * Tb, "capacity:1", "Y"),
                Sample(20, 3 * Tb, "capacity:2", "Z"), Sample(10, 2 * Tb, "capacity:2", "Z"), Sample(0, 1 * Tb, "capacity:2", "Z"),
            ]);

        Result<List<StorageForecast>> result = await Forecaster().Handle();

        result.Value.Select(f => f.VolumeId).Should().Equal("capacity:2", "capacity:1");
        result.Value[1].DaysUntilFull.Should().BeNull();
    }
}
