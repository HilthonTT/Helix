using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Queries;

public sealed class LookUpMacAddress(ILoggedInUser loggedInUser, IWakeOnLan wakeOnLan) : IHandler
{
    public sealed record Request(string Host);

    public async Task<Result<string>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        if (!GeneralValidation.IsValidHost(request.Host))
        {
            return Result.Failure<string>(ValidationErrors.InvalidHost);
        }

        string? macAddress = await wakeOnLan.LookUpAsync(request.Host, cancellationToken);

        return macAddress is null
            ? Result.Failure<string>(DriveErrors.MacAddressNotFound(request.Host))
            : Result.Success(macAddress);
    }
}
