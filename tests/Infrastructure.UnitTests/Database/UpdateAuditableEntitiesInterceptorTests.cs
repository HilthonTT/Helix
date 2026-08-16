using FluentAssertions;
using Helix.Domain.Users;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Infrastructure.UnitTests.Database;

public sealed class UpdateAuditableEntitiesInterceptorTests : IDisposable
{
    private static readonly DateTime InsertedAt = new(2024, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime UpdatedAt = new(2024, 3, 2, 9, 30, 0, DateTimeKind.Utc);

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly IDateTimeProvider _dateTimeProvider = Substitute.For<IDateTimeProvider>();

    public UpdateAuditableEntitiesInterceptorTests()
    {
        _connection.Open();
    }

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new UpdateAuditableEntitiesInterceptor(_dateTimeProvider))
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    [Fact]
    public async Task SaveChanges_Should_StampCreatedOn_When_EntityIsAdded()
    {
        _dateTimeProvider.UtcNow.Returns(InsertedAt);

        using AppDbContext context = CreateContext();

        User user = User.Create("ada", "hash");
        context.Users.Add(user);

        await context.SaveChangesAsync(CancellationToken.None);

        user.CreatedOnUtc.Should().Be(InsertedAt);
        user.ModifiedOnUtc.Should().Be(InsertedAt);
    }

    [Fact]
    public async Task SaveChanges_Should_AdvanceModifiedOn_And_KeepCreatedOn_When_EntityChanges()
    {
        _dateTimeProvider.UtcNow.Returns(InsertedAt);

        using AppDbContext context = CreateContext();

        User user = User.Create("ada", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        _dateTimeProvider.UtcNow.Returns(UpdatedAt);

        user.Update("ada.lovelace");
        await context.SaveChangesAsync(CancellationToken.None);

        user.CreatedOnUtc.Should().Be(InsertedAt);
        user.ModifiedOnUtc.Should().Be(UpdatedAt);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
