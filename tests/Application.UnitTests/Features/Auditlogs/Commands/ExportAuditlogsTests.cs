using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Core.Errors;
using Helix.Application.Features.Auditlogs.Commands;
using Helix.Domain.Auditlogs;
using NSubstitute;

namespace Application.UnitTests.Features.Auditlogs.Commands;

public sealed class ExportAuditlogsTests : IDisposable
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ExportAuditlogs.Request Request = new(
        ["Time", "Action", "Letter", "Drive", "Description"],
        row => $"described {row.EntityName}");

    private readonly IAuditlogRepository _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IFolderPicker _folderPickerMock = Substitute.For<IFolderPicker>();
    private readonly IDateTimeProvider _dateTimeProviderMock = Substitute.For<IDateTimeProvider>();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"helix-export-{Guid.NewGuid():N}");

    private readonly ExportAuditlogs _handler;

    public ExportAuditlogsTests()
    {
        Directory.CreateDirectory(_folder);

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _dateTimeProviderMock.UtcNow.Returns(Now);
        _folderPickerMock.PickAsync(Arg.Any<CancellationToken>()).Returns(new FolderPick(true, _folder));

        _handler = new(_auditlogRepositoryMock, _loggedInUserMock, _folderPickerMock, _dateTimeProviderMock);
    }

    public void Dispose()
    {
        Directory.Delete(_folder, recursive: true);
    }

    private static Auditlog Row(string name, DateTime atUtc)
    {
        var row = Auditlog.ForDrive(UserId, AuditAction.DriveDisconnected, Guid.NewGuid(), name, "Z");
        row.CreatedOnUtc = atUtc;

        return row;
    }

    private void GivenHistory(params Auditlog[] rows) =>
        _auditlogRepositoryMock
            .GetPageAsNoTrackingAsync(UserId, 0, int.MaxValue, true, Arg.Any<CancellationToken>())
            .Returns([.. rows]);

    [Fact]
    public async Task Handle_Should_WriteEveryRowWithAHeader()
    {
        GivenHistory(Row("Media", Now.AddHours(-2)), Row("Backups", Now.AddHours(-1)));

        Result<string> result = await _handler.Handle(Request);

        string[] lines = File.ReadAllLines(result.Value);
        lines.Should().HaveCount(3);
        lines[0].Should().Be("Time,Action,Letter,Drive,Description");
        lines[1].Should().Be("2026-10-18 10:00:00,DriveDisconnected,Z,Media,described Media");
    }

    [Fact]
    public async Task Handle_Should_ReportNothingToExport_WithoutOpeningThePicker()
    {
        GivenHistory();

        Result<string> result = await _handler.Handle(Request);

        result.Error.Should().Be(AuditlogErrors.NothingToExport);
        await _folderPickerMock.DidNotReceiveWithAnyArgs().PickAsync(default);
    }

    [Fact]
    public async Task Handle_Should_StopQuietly_WhenThePickerIsDismissed()
    {
        GivenHistory(Row("Media", Now));
        _folderPickerMock.PickAsync(Arg.Any<CancellationToken>()).Returns(new FolderPick(false, null));

        Result<string> result = await _handler.Handle(Request);

        result.Error.Should().Be(FolderPickerErrors.Cancelled);
        Directory.EnumerateFiles(_folder).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Media", "Media")]
    [InlineData("Media, Photos", "\"Media, Photos\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "'+1")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    public void Escape_Should_QuoteSeparatorsAndDefuseFormulas(string value, string expected)
    {
        ExportAuditlogs.Escape(value).Should().Be(expected);
    }
}
