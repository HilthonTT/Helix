using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class ListShares(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    public sealed record Request(string Host, string Username, string Password);

    public async Task<Result<List<AvailableShare>>> Handle(
        Request request,
        CancellationToken cancellationToken = default)
    {
        if (!GeneralValidation.IsValidHost(request.Host))
        {
            return Result.Failure<List<AvailableShare>>(ValidationErrors.InvalidHost);
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<List<AvailableShare>>(ValidationErrors.MissingFields);
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<AvailableShare>>(AuthenticationErrors.InvalidPermissions);
        }

        Result<IReadOnlyList<string>> listed = await nasConnector.ListSharesAsync(
            request.Host.Trim(),
            request.Username.Trim(),
            request.Password,
            cancellationToken);

        if (listed.IsFailure)
        {
            return Result.Failure<List<AvailableShare>>(listed.Error);
        }

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        Dictionary<string, string> existing = new(StringComparer.OrdinalIgnoreCase);

        foreach (Drive drive in drives.Where(d => d.IsOnSameServerAs(request.Host)))
        {
            existing.TryAdd(drive.Name, drive.Letter);
        }

        return listed.Value
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => new AvailableShare(name, existing.GetValueOrDefault(name)))
            .ToList();
    }
}
