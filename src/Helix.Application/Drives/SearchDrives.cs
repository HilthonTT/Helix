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

        // Scope to the logged-in user — without this filter the search would leak
        // other users' drives (including their NAS credentials).
        IQueryable<Drive> drivesQuery = context.Drives
            .AsNoTracking()
            .Where(d => d.UserId == loggedInUser.UserId);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            // Letters are stored uppercase; upper-case both sides so the search
            // box matches regardless of the casing the user types.
            string searchTerm = request.SearchTerm.ToUpperInvariant();

            drivesQuery = drivesQuery
                .Where(d => d.Name.ToUpper().Contains(searchTerm) ||
                       d.Letter.Contains(searchTerm));
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
