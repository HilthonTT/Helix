using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Sorting;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Helix.Application.Features.Auditlogs.Queries;

public sealed class SearchAuditlogs(IDbContext context, ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string SearchTerm, SortOrder SortOrder);

    public async Task<Result<List<Auditlog>>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Auditlog>>(AuthenticationErrors.InvalidPermissions);
        }

        // Scope to the logged-in user — without this filter the search would leak
        // other users' audit logs.
        IQueryable<Auditlog> auditlogsQuery = context.AuditLogs
            .AsNoTracking()
            .Where(a => a.UserId == loggedInUser.UserId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            auditlogsQuery = auditlogsQuery.Where(a => a.Message.Contains(request.SearchTerm));
        }

        if (request.SortOrder == SortOrder.Descending)
        {
            auditlogsQuery = auditlogsQuery.OrderByDescending(d => d.CreatedOnUtc);
        }
        else
        {
            auditlogsQuery = auditlogsQuery.OrderBy(d => d.CreatedOnUtc);
        }

        List<Auditlog> auditlogs = await auditlogsQuery.ToListAsync(cancellationToken);

        return auditlogs;
    }
}
