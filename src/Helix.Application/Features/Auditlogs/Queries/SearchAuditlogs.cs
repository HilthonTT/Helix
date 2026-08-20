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
            string term = request.SearchTerm;

            // Matched against the drive it happened to rather than the rendered sentence:
            // the sentence is composed at display time in the user's language and does
            // not exist in the database to search. Message still carries the text of
            // entries written before the log was structured, so it stays in the filter.
            auditlogsQuery = auditlogsQuery.Where(a =>
                (a.EntityName != null && a.EntityName.Contains(term)) ||
                (a.EntityLetter != null && a.EntityLetter.Contains(term)) ||
                (a.Detail != null && a.Detail.Contains(term)) ||
                (a.Message != null && a.Message.Contains(term)));
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
