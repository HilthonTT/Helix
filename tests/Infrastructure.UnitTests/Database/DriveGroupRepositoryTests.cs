using FluentAssertions;
using Helix.Domain.DriveGroups;
using Helix.Domain.Users;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.UnitTests.Database;

/// <summary>
/// Covers the name check behind "a group called X already exists", and the round trip of
/// the membership column.
/// </summary>
/// <remarks>
/// Against real SQLite rather than mocks, because both are questions about what the
/// database does — how it compares text, and how it stores a list of ids in one column —
/// which is exactly what a substituted repository cannot answer.
/// </remarks>
public sealed class DriveGroupRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public DriveGroupRepositoryTests()
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

    private static async Task<Guid> SeedGroupAsync(AppDbContext context, Guid userId, string name)
    {
        DriveGroup group = DriveGroup.Create(userId, name, [Guid.CreateVersion7()]);

        context.DriveGroups.Add(group);
        await context.SaveChangesAsync(CancellationToken.None);

        return group.Id;
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_RejectAnExistingName()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        await SeedGroupAsync(context, userId, "Office");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("Office", userId)).Should().BeFalse();
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_RejectANameThatDiffersOnlyInCase()
    {
        // "Office" and "office" are the same group to anyone reading a row of buttons.
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        await SeedGroupAsync(context, userId, "Office");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("office", userId)).Should().BeFalse();
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_NotReadTheNameAsAPattern()
    {
        // The check used LIKE, which reads its right-hand side as a pattern: an
        // underscore matched any character, so this name was refused as a duplicate of a
        // group nothing was using it for.
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        await SeedGroupAsync(context, userId, "HomeXNAS");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("Home_NAS", userId)).Should().BeTrue();
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_NotTreatAPercentSignAsAWildcard()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        await SeedGroupAsync(context, userId, "Backup 100 percent");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("Backup %", userId)).Should().BeTrue();
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_LetAGroupKeepItsOwnName()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        Guid groupId = await SeedGroupAsync(context, userId, "Office");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("Office", userId, groupId)).Should().BeTrue();
    }

    [Fact]
    public async Task IsNameUniqueAsync_Should_KeepUsersApart()
    {
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);
        await SeedGroupAsync(context, userId, "Office");

        var repository = new DriveGroupRepository(context);

        (await repository.IsNameUniqueAsync("Office", Guid.CreateVersion7())).Should().BeTrue();
    }

    [Fact]
    public async Task Membership_Should_SurviveARoundTrip()
    {
        // Stored as a primitive collection in one column rather than a join table; worth
        // proving it comes back in the order it went in, since that order is what the
        // group's connect pass follows.
        using AppDbContext context = CreateContext();
        Guid userId = await SeedUserAsync(context);

        Guid first = Guid.CreateVersion7();
        Guid second = Guid.CreateVersion7();

        DriveGroup group = DriveGroup.Create(userId, "Office", [first, second]);
        context.DriveGroups.Add(group);
        await context.SaveChangesAsync(CancellationToken.None);

        using AppDbContext reader = CreateContext();

        DriveGroup? stored = await new DriveGroupRepository(reader).GetByIdAsNoTrackingAsync(group.Id);

        stored.Should().NotBeNull();
        stored!.DriveIds.Should().Equal(first, second);
    }
}
