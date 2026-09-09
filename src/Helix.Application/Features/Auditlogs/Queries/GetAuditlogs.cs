using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Sorting;
using Helix.Application.Features.Auditlogs.Contracts;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;

namespace Helix.Application.Features.Auditlogs.Queries;

public sealed class GetAuditlogs(
    IAuditlogRepository auditlogRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public const int DefaultPageSize = 100;

    public const int MaximumPageSize = 1000;

    public sealed record Request(int Skip = 0, int Take = DefaultPageSize, SortOrder Order = SortOrder.Descending);

    public async Task<Result<AuditlogPage>> Handle(
        Request? request = null,
        CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<AuditlogPage>(AuthenticationErrors.InvalidPermissions);
        }

        request ??= new Request();

        // Clamped rather than refused: the page size is the app's own, not something
        // typed in, and a query that answers nothing is worse than one that answers less.
        int skip = Math.Max(0, request.Skip);
        int take = Math.Clamp(request.Take, 1, MaximumPageSize);

        int total = await auditlogRepository.CountAsync(loggedInUser.UserId, cancellationToken);

        if (skip >= total)
        {
            return new AuditlogPage([], skip, total);
        }

        List<Auditlog> auditlogs = await auditlogRepository.GetPageAsNoTrackingAsync(
            loggedInUser.UserId,
            skip,
            take,
            request.Order == SortOrder.Ascending,
            cancellationToken);

        return new AuditlogPage(auditlogs, skip, total);
    }
}
