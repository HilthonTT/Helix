using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using SharedKernel;

namespace Helix.Application.Auditlogs;

public sealed class GetAuditlogs(
    IAuditlogRepository auditlogRepository,
    ILoggedInUser loggedInUser) : IHandler
{
    public async Task<Result<List<Auditlog>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Auditlog>>(AuthenticationErrors.InvalidPermissions);
        }

        List<Auditlog> auditLogs = await auditlogRepository.GetAsNoTrackingAsync(
            loggedInUser.UserId, 
            cancellationToken);

        return auditLogs;
    }
}
