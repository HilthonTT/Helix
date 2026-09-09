using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Core.Sorting;
using Helix.Application.Features.Auditlogs.Contracts;
using Helix.Application.Features.Auditlogs.Queries;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using NSubstitute;

namespace Application.UnitTests.Features.Auditlogs.Queries;

public sealed class GetAuditlogsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly GetAuditlogs _getAuditlogs;

    private readonly IAuditlogRepository _auditlogRepositoryMock;
    private readonly ILoggedInUser _loggedInUserMock;

    public GetAuditlogsTests()
    {
        _auditlogRepositoryMock = Substitute.For<IAuditlogRepository>();
        _loggedInUserMock = Substitute.For<ILoggedInUser>();

        _loggedInUserMock.UserId.Returns(UserId);
        _loggedInUserMock.IsLoggedIn.Returns(true);

        _getAuditlogs = new(_auditlogRepositoryMock, _loggedInUserMock);
    }

    private void HaveHistory(int total, params Auditlog[] page)
    {
        _auditlogRepositoryMock.CountAsync(UserId, Arg.Any<CancellationToken>()).Returns(total);

        _auditlogRepositoryMock.GetPageAsNoTrackingAsync(
            UserId,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>()).Returns([.. page]);
    }

    [Fact]
    public async Task Handle_Should_ReturnError_WhenNotSignedIn()
    {
        _loggedInUserMock.IsLoggedIn.Returns(false);

        Result<AuditlogPage> result = await _getAuditlogs.Handle();

        result.Error.Should().Be(AuthenticationErrors.InvalidPermissions);
    }

    [Fact]
    public async Task Handle_Should_AskForTheDefaultPage_WhenNoRequestIsGiven()
    {
        HaveHistory(total: 500);

        await _getAuditlogs.Handle();

        await _auditlogRepositoryMock.Received(1).GetPageAsNoTrackingAsync(
            UserId,
            0,
            GetAuditlogs.DefaultPageSize,
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_AskForTheOldestFirst_WhenTheOrderIsAscending()
    {
        HaveHistory(total: 10);

        await _getAuditlogs.Handle(new GetAuditlogs.Request(0, 10, SortOrder.Ascending));

        await _auditlogRepositoryMock.Received(1).GetPageAsNoTrackingAsync(
            UserId,
            0,
            10,
            true,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ClampThePageSize()
    {
        HaveHistory(total: 10_000);

        await _getAuditlogs.Handle(new GetAuditlogs.Request(-5, int.MaxValue));

        await _auditlogRepositoryMock.Received(1).GetPageAsNoTrackingAsync(
            UserId,
            0,
            GetAuditlogs.MaximumPageSize,
            false,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_ReportMore_WhenThePageDoesNotReachTheTotal()
    {
        HaveHistory(total: 250, page: [.. Enumerable.Range(0, 100).Select(_ => AnEntry())]);

        Result<AuditlogPage> result = await _getAuditlogs.Handle();

        result.Value.TotalCount.Should().Be(250);
        result.Value.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Should_NotReportMore_WhenThePageReachesTheTotal()
    {
        HaveHistory(total: 100, page: [.. Enumerable.Range(0, 100).Select(_ => AnEntry())]);

        Result<AuditlogPage> result = await _getAuditlogs.Handle();

        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_Should_NotQueryAPageThatIsPastTheEnd()
    {
        HaveHistory(total: 40);

        Result<AuditlogPage> result = await _getAuditlogs.Handle(new GetAuditlogs.Request(40, 100));

        result.Value.Items.Should().BeEmpty();
        result.Value.HasMore.Should().BeFalse();

        await _auditlogRepositoryMock.DidNotReceive().GetPageAsNoTrackingAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    private static Auditlog AnEntry() =>
        Auditlog.ForDrive(UserId, AuditAction.DriveReconnected, Guid.NewGuid(), "Drive", "Z");
}
