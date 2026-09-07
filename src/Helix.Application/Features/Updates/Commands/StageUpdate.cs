using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Commands;

public sealed class StageUpdate(
    ILoggedInUser loggedInUser,
    IUpdateInstaller updateInstaller) : IHandler
{
    public sealed record Request(UpdateCheck Update, IProgress<double>? Progress = null);

    public async Task<Result<string>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        if (!request.Update.CanInstall)
        {
            return Result.Failure<string>(UpdateErrors.NoAsset);
        }

        return await updateInstaller.StageAsync(request.Update, request.Progress, cancellationToken);
    }
}
