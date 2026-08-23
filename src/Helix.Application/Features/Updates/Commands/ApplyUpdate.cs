using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Commands;

/// <summary>
/// Hands a staged update to the helper that replaces the install.
/// </summary>
/// <remarks>
/// Success here means the helper started, not that the update is installed — the helper
/// is waiting for this process to exit before it does anything. The caller must quit
/// immediately, and nothing after this point can be undone from inside Helix.
/// </remarks>
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
