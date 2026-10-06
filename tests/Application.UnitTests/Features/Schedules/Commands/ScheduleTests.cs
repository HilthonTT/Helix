using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Time;
using Helix.Application.Features.Schedules.Commands;
using Helix.Application.Features.Schedules.Contracts;
using Helix.Domain.DriveGroups;
using Helix.Domain.Drives;
using Helix.Domain.Schedules;
using NSubstitute;

namespace Application.UnitTests.Features.Schedules.Commands;

public class ScheduleTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTime MondayMorning = new(2026, 10, 5, 6, 0, 0, DateTimeKind.Utc);

    private static readonly TimeZoneInfo Brussels = TimeZoneInfo.CreateCustomTimeZone(
        "Test/Plus2", TimeSpan.FromHours(2), "Plus two", "Plus two");

    private static readonly TimeSpan Grace = RunDueSchedules.Grace;

    private readonly IScheduleRepository _scheduleRepositoryMock;
    private readonly IDriveGroupRepository _driveGroupRepositoryMock;
    private readonly IDriveRepository _driveRepositoryMock;
    private readonly IUnitOfWork _unitOfWorkMock;
    private readonly ILoggedInUser _loggedInUserMock;
    private readonly INasConnector _nasConnectorMock;
    private readonly IDriveMonitor _driveMonitorMock;
    private readonly INetworkLocation _networkLocationMock;
    private readonly IDateTimeProvider _dateTimeProviderMock;
    private readonly ILocalTimeZone _localTimeZoneMock;

    private readonly Drive _media;
    private readonly Drive _backups;

    public ScheduleTests()
    {
        _scheduleRepositoryMock = Substitute.For<IScheduleRepository>();
        _driveGroupRepositoryMock = Substitute.For<IDriveGroupRepository>();
        _driveRepositoryMock = Substitute.For<IDriveRepository>();
        _unitOfWorkMock = Substitute.For<IUnitOfWork>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();
        _nasConnectorMock = Substitute.For<INasConnector>();
        _driveMonitorMock = Substitute.For<IDriveMonitor>();
        _networkLocationMock = Substitute.For<INetworkLocation>();
        _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();
        _localTimeZoneMock = Substitute.For<ILocalTimeZone>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _localTimeZoneMock.Current.Returns(Brussels);

        _media = Drive.Create(UserId, "Z", "192.168.0.1", "Media Vault", "Username", "Password");
        _backups = Drive.Create(UserId, "Y", "192.168.0.1", "Backups", "Username", "Password");

        _driveRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns([_media, _backups]);

        _nasConnectorMock.GetConnectedLetters().Returns([]);
        _nasConnectorMock.ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _nasConnectorMock.DisconnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    private static Schedule GivenSchedule(
        string time,
        ScheduleDays days = ScheduleDays.Weekdays,
        ScheduleAction action = ScheduleAction.Connect,
        Guid? groupId = null,
        DateTime? createdOnUtc = null)
    {
        var schedule = Schedule.Create(UserId, groupId, action, TimeOnly.Parse(time), days);

        schedule.CreatedOnUtc = createdOnUtc ?? MondayMorning.AddDays(-7);
        schedule.ModifiedOnUtc = schedule.CreatedOnUtc;

        return schedule;
    }

    private RunDueSchedules Run(DateTime utcNow, params Schedule[] schedules)
    {
        _dateTimeProviderMock.UtcNow.Returns(utcNow);
        _scheduleRepositoryMock.GetAsync(UserId, Arg.Any<CancellationToken>()).Returns([.. schedules]);

        return new RunDueSchedules(
            _scheduleRepositoryMock,
            _driveGroupRepositoryMock,
            _driveRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock,
            _nasConnectorMock,
            _driveMonitorMock,
            _networkLocationMock,
            _dateTimeProviderMock,
            _localTimeZoneMock);
    }

    [Fact]
    public void DueOccurrence_Should_BeTheLocalTime_OnceItHasPassed()
    {
        Schedule schedule = GivenSchedule("08:00");

        DateTime? due = schedule.DueOccurrence(MondayMorning.AddMinutes(1), Brussels, Grace);

        due.Should().Be(MondayMorning);
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_BeforeTheTime()
    {
        Schedule schedule = GivenSchedule("08:00");

        schedule.DueOccurrence(MondayMorning.AddMinutes(-1), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_OnceTheGraceHasPassed()
    {
        Schedule schedule = GivenSchedule("08:00");

        schedule.DueOccurrence(MondayMorning + Grace + TimeSpan.FromMinutes(1), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_OnADayThatIsNotSelected()
    {
        Schedule schedule = GivenSchedule("08:00", ScheduleDays.Weekend);

        schedule.DueOccurrence(MondayMorning.AddMinutes(1), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_OnceItHasRun()
    {
        Schedule schedule = GivenSchedule("08:00");

        schedule.MarkRun(MondayMorning);

        schedule.DueOccurrence(MondayMorning.AddMinutes(2), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_ForATimeThatHadPassedWhenTheScheduleWasSaved()
    {
        Schedule schedule = GivenSchedule("08:00", createdOnUtc: MondayMorning.AddMinutes(3));

        schedule.DueOccurrence(MondayMorning.AddMinutes(4), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_BeNothing_WhenDisabled()
    {
        Schedule schedule = GivenSchedule("08:00");

        schedule.Update(null, ScheduleAction.Connect, TimeOnly.Parse("08:00"), ScheduleDays.Weekdays, isEnabled: false);

        schedule.DueOccurrence(MondayMorning.AddMinutes(1), Brussels, Grace).Should().BeNull();
    }

    [Fact]
    public void DueOccurrence_Should_FollowTheLocalDate_AcrossMidnight()
    {
        Schedule schedule = GivenSchedule("00:30", ScheduleDays.Monday);

        DateTime mondayHalfPastMidnightLocal = new(2026, 10, 4, 22, 30, 0, DateTimeKind.Utc);

        schedule.DueOccurrence(mondayHalfPastMidnightLocal.AddMinutes(1), Brussels, Grace)
            .Should().Be(mondayHalfPastMidnightLocal);
    }

    [Fact]
    public async Task Run_Should_ConnectEveryDrive_AndStampTheSchedule()
    {
        Schedule schedule = GivenSchedule("08:00");

        Result<List<ScheduleRun>> result = await Run(MondayMorning.AddMinutes(1), schedule).Handle();

        result.Value.Should().ContainSingle().Which.Outcome.IsSuccess.Should().BeTrue();
        schedule.LastRunOnUtc.Should().Be(MondayMorning);

        await _nasConnectorMock.Received(1).ConnectAsync(_media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(_backups, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Should_DisconnectOnlyTheGroupsDrives()
    {
        DriveGroup group = DriveGroup.Create(UserId, "Office", [_backups.Id]);
        _driveGroupRepositoryMock.GetAsNoTrackingAsync(UserId, Arg.Any<CancellationToken>()).Returns([group]);

        _nasConnectorMock.GetConnectedLetters().Returns(["Z", "Y"]);
        _nasConnectorMock.IsMountedFrom(Arg.Any<Drive>()).Returns(true);

        Schedule schedule = GivenSchedule("08:00", action: ScheduleAction.Disconnect, groupId: group.Id);

        Result<List<ScheduleRun>> result = await Run(MondayMorning.AddMinutes(1), schedule).Handle();

        ScheduleRun run = result.Value.Should().ContainSingle().Subject;
        run.GroupName.Should().Be("Office");
        run.DriveIds.Should().Equal(_backups.Id);

        await _nasConnectorMock.Received(1).DisconnectAsync(_backups, Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().DisconnectAsync(_media, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Should_LeaveADrivePinnedElsewhere_Alone()
    {
        _media.PinToNetwork("gateway:home", "Home");
        _networkLocationMock.GetCurrentAsync(Arg.Any<CancellationToken>())
            .Returns(new NetworkLocation("gateway:cafe", "Cafe"));

        Schedule schedule = GivenSchedule("08:00");

        await Run(MondayMorning.AddMinutes(1), schedule).Handle();

        await _nasConnectorMock.DidNotReceive().ConnectAsync(_media, Arg.Any<CancellationToken>());
        await _nasConnectorMock.Received(1).ConnectAsync(_backups, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Run_Should_DoNothing_WhenNothingIsDue()
    {
        Schedule schedule = GivenSchedule("09:00");

        Result<List<ScheduleRun>> result = await Run(MondayMorning.AddMinutes(1), schedule).Handle();

        result.Value.Should().BeEmpty();
        await _unitOfWorkMock.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _nasConnectorMock.DidNotReceive().ConnectAsync(Arg.Any<Drive>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_Should_Refuse_AScheduleWithNoDays()
    {
        var create = new CreateSchedule(
            _scheduleRepositoryMock,
            _driveGroupRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock);

        Result<Schedule> result = await create.Handle(
            new CreateSchedule.Request(null, ScheduleAction.Connect, TimeOnly.Parse("08:00"), ScheduleDays.None));

        result.Error.Should().Be(ScheduleErrors.NoDaysSelected);
    }

    [Fact]
    public async Task Create_Should_Refuse_AGroupThatIsNotTheUsers()
    {
        DriveGroup someoneElses = DriveGroup.Create(Guid.NewGuid(), "Theirs", [Guid.NewGuid()]);
        _driveGroupRepositoryMock.GetByIdAsNoTrackingAsync(someoneElses.Id, Arg.Any<CancellationToken>())
            .Returns(someoneElses);

        var create = new CreateSchedule(
            _scheduleRepositoryMock,
            _driveGroupRepositoryMock,
            _unitOfWorkMock,
            _loggedInUserMock);

        Result<Schedule> result = await create.Handle(
            new CreateSchedule.Request(someoneElses.Id, ScheduleAction.Connect, TimeOnly.Parse("08:00"), ScheduleDays.Weekdays));

        result.Error.Should().Be(DriveGroupErrors.NotFound(someoneElses.Id));
    }
}
