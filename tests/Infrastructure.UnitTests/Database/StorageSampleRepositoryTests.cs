using FluentAssertions;
using Helix.Domain.Storage;
using Helix.Domain.Users;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.UnitTests.Database;

public sealed class StorageSampleRepositoryTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 18);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public StorageSampleRepositoryTests()
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

    private static StorageSample Sample(Guid userId, int daysAgo, string volumeId = "capacity:1") =>
        StorageSample.Create(userId, volumeId, Today.AddDays(-daysAgo), 1_000, 500, "Z", DateTime.UtcNow);

    [Fact]
    public async Task GetSinceAsync_Should_ReturnTheWindowInDayOrder()
    {
        Guid userId;

        using (AppDbContext context = CreateContext())
        {
            userId = await SeedUserAsync(context);

            var repository = new StorageSampleRepository(context);
            repository.Insert(Sample(userId, 0));
            repository.Insert(Sample(userId, 40));
            repository.Insert(Sample(userId, 5));

            await context.SaveChangesAsync(CancellationToken.None);
        }

        using AppDbContext fresh = CreateContext();

        List<StorageSample> samples = await new StorageSampleRepository(fresh).GetSinceAsync(userId, Today.AddDays(-30));

        samples.Select(s => s.Day).Should().Equal(Today.AddDays(-5), Today);
    }

    [Fact]
    public async Task Insert_Should_RefuseASecondSampleForOneVolumeOnOneDay()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);

        var repository = new StorageSampleRepository(context);
        repository.Insert(Sample(userId, 0));
        repository.Insert(Sample(userId, 0));

        Func<Task> save = () => context.SaveChangesAsync(CancellationToken.None);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task RemoveOlderThanAsync_Should_KeepTheRecentHistory()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);

        var repository = new StorageSampleRepository(context);
        repository.Insert(Sample(userId, 500));
        repository.Insert(Sample(userId, 10));
        await context.SaveChangesAsync(CancellationToken.None);

        int removed = await repository.RemoveOlderThanAsync(userId, Today.AddDays(-400));

        removed.Should().Be(1);
        (await repository.GetSinceAsync(userId, DateOnly.MinValue)).Should().ContainSingle();
    }
}
