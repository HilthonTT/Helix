using FluentAssertions;
using Helix.Domain.Auditlogs;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Helix.Infrastructure.Database;
using Helix.Infrastructure.Database.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.UnitTests.Database;

/// <summary>
/// Covers what the audit log records, and — just as important — what it declines to.
/// </summary>
public sealed class InsertAuditLogsInterceptorTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    private Guid _userId;

    public InsertAuditLogsInterceptorTests()
    {
        _connection.Open();
    }

    private AppDbContext CreateContext()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new InsertAuditLogsInterceptor())
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    /// <summary>A saved user for the drive's foreign key to point at.</summary>
    private async Task<Drive> SeedDriveAsync(AppDbContext context)
    {
        User user = User.Create("ada", "hash");
        context.Users.Add(user);
        await context.SaveChangesAsync(CancellationToken.None);

        _userId = user.Id;

        Drive drive = Drive.Create(_userId, "Z", "nas.local", "Media", "user", "password");
        context.Drives.Add(drive);
        await context.SaveChangesAsync(CancellationToken.None);

        return drive;
    }

    private static Task<List<Auditlog>> EntriesAsync(AppDbContext context) =>
        context.AuditLogs.AsNoTracking().OrderBy(a => a.CreatedOnUtc).ToListAsync();

    [Fact]
    public async Task SaveChanges_Should_RecordTheStructuredFields_WhenADriveIsAdded()
    {
        using AppDbContext context = CreateContext();

        Drive drive = await SeedDriveAsync(context);

        List<Auditlog> entries = await EntriesAsync(context);

        Auditlog entry = entries.Should().ContainSingle().Subject;

        entry.Action.Should().Be(AuditAction.DriveCreated);
        entry.EntityId.Should().Be(drive.Id);
        entry.EntityName.Should().Be("Media");
        entry.EntityLetter.Should().Be("Z");

        // Nothing composes prose any more; the sentence is built at display time.
        entry.Message.Should().BeNull();
    }

    [Fact]
    public async Task SaveChanges_Should_RecordAnUpdate_WhenTheUserChangesTheDrive()
    {
        using AppDbContext context = CreateContext();

        Drive drive = await SeedDriveAsync(context);

        drive.Update("Y", "nas.local", "Media", "user", "password", autoConnect: true, persistent: false);
        await context.SaveChangesAsync(CancellationToken.None);

        List<Auditlog> entries = await EntriesAsync(context);

        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveUpdated);
    }

    /// <summary>
    /// The reason the filter exists: every connect stamps the drive, and without this
    /// each one would file a "the drive was changed" entry and bury the real events.
    /// </summary>
    [Fact]
    public async Task SaveChanges_Should_RecordNothing_WhenOnlyTheConnectionStampMoves()
    {
        using AppDbContext context = CreateContext();

        Drive drive = await SeedDriveAsync(context);

        drive.MarkConnected(new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        await context.SaveChangesAsync(CancellationToken.None);

        List<Auditlog> entries = await EntriesAsync(context);

        // Only the creation entry from the seed.
        entries.Should().ContainSingle().Which.Action.Should().Be(AuditAction.DriveCreated);
    }

    [Fact]
    public async Task SaveChanges_Should_StillRecord_WhenAConnectIsSavedAlongsideARealChange()
    {
        using AppDbContext context = CreateContext();

        Drive drive = await SeedDriveAsync(context);

        drive.MarkConnected(new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        drive.Update("Z", "nas.local", "Media Vault", "user", "password", autoConnect: true, persistent: false);

        await context.SaveChangesAsync(CancellationToken.None);

        List<Auditlog> entries = await EntriesAsync(context);

        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveUpdated);
        entries[1].EntityName.Should().Be("Media Vault");
    }

    [Fact]
    public async Task SaveChanges_Should_RecordADeletion_ThatOutlivesTheDrive()
    {
        using AppDbContext context = CreateContext();

        Drive drive = await SeedDriveAsync(context);

        context.Drives.Remove(drive);
        await context.SaveChangesAsync(CancellationToken.None);

        List<Auditlog> entries = await EntriesAsync(context);

        entries.Should().HaveCount(2);
        entries[1].Action.Should().Be(AuditAction.DriveDeleted);

        // The name is copied into the entry, not read back through a relationship —
        // there is no drive left to read it from.
        entries[1].EntityName.Should().Be("Media");
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
