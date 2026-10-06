using FluentAssertions;
using Helix.Domain.DriveGroups;
using Helix.Domain.Schedules;
using Helix.Domain.Users;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.UnitTests.Database;

public sealed class ScheduleRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ScheduleRepositoryTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    private static async Task<Guid> SeedUserAsync(AppDbContext context)
    {
        User user = User.Create("ada", "hash");

        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        return user.Id;
    }

    [Fact]
    public async Task GetAsNoTrackingAsync_Should_RoundTripTheTimeAndDays()
    {
        Guid userId;

        using (AppDbContext context = CreateContext())
        {
            userId = await SeedUserAsync(context);

            var repository = new ScheduleRepository(context);
            repository.Insert(Schedule.Create(
                userId,
                null,
                ScheduleAction.Disconnect,
                new TimeOnly(23, 30),
                ScheduleDays.Friday | ScheduleDays.Saturday));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        using AppDbContext fresh = CreateContext();

        List<Schedule> schedules = await new ScheduleRepository(fresh).GetAsNoTrackingAsync(userId);

        Schedule schedule = schedules.Should().ContainSingle().Subject;
        schedule.TimeOfDay.Should().Be(new TimeOnly(23, 30));
        schedule.Days.Should().Be(ScheduleDays.Friday | ScheduleDays.Saturday);
        schedule.Action.Should().Be(ScheduleAction.Disconnect);
        schedule.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task DeletingAGroup_Should_DeleteTheSchedulesThatNameIt()
    {
        Guid userId;
        Guid groupId;

        using (AppDbContext context = CreateContext())
        {
            userId = await SeedUserAsync(context);

            DriveGroup group = DriveGroup.Create(userId, "Office", [Guid.CreateVersion7()]);
            context.DriveGroups.Add(group);
            groupId = group.Id;

            var repository = new ScheduleRepository(context);
            repository.Insert(Schedule.Create(userId, groupId, ScheduleAction.Connect, new TimeOnly(8, 0), ScheduleDays.Weekdays));
            repository.Insert(Schedule.Create(userId, null, ScheduleAction.Disconnect, new TimeOnly(23, 0), ScheduleDays.Everyday));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        using (AppDbContext context = CreateContext())
        {
            DriveGroup group = await context.DriveGroups.SingleAsync(g => g.Id == groupId);

            new DriveGroupRepository(context).Remove(group);

            await context.SaveChangesAsync(CancellationToken.None);
        }

        using AppDbContext fresh = CreateContext();

        List<Schedule> remaining = await new ScheduleRepository(fresh).GetAsNoTrackingAsync(userId);

        remaining.Should().ContainSingle().Which.DriveGroupId.Should().BeNull();
    }
}
