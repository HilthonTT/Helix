using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Auditlogs;

public sealed class GetAuditlogs(IDbContext context, ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result<List<Auditlog>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Auditlog>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Auditlog> auditLogs = await context
           .AuditLogs
           .Where(a => a.UserId == loggedInUser.UserId)
           .OrderByDescending(a => a.CreatedOnUtc)
           .ToListAsync(cancellationToken);

        return auditLogs;
    }
}
