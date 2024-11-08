using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Sorting;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Drives;

public sealed class SearchDrives(
    IDbContext context, 
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(string SearchTerm, SortOrder SortOrder);

    public async Task<Result<List<Drive>>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<Drive>>(AuthenticationErrors.InvalidPermissions);
        }

        IQueryable<Drive> drivesQuery = context.Drives
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            drivesQuery = drivesQuery
                .Where(d => d.Name.Contains(request.SearchTerm) ||
                       d.Letter.Contains(request.SearchTerm));
        }

        if (request.SortOrder == SortOrder.Descending)
        {
            drivesQuery = drivesQuery.OrderByDescending(d => d.Name);
        }
        else
        {
            drivesQuery = drivesQuery.OrderBy(d => d.Name);
        }

        List<Drive> drives = await drivesQuery.ToListAsync(cancellationToken);

        return drives;
    }
}
