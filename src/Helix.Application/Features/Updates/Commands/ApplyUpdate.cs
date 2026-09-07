using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Commands;

public sealed class ApplyUpdate(
    ILoggedInUser loggedInUser,
    IUpdateInstaller updateInstaller) : IHandler
{
    public sealed record Request(string StagedDirectory);

    public Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Task.FromResult(Result.Failure(AuthenticationErrors.InvalidPermissions));
        }

        return Task.FromResult(updateInstaller.Apply(request.StagedDirectory));
    }
}
