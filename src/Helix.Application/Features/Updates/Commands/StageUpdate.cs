using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Updates;
using Helix.Domain.Users;

namespace Helix.Application.Features.Updates.Commands;

/// <summary>
/// Downloads a release and unpacks it, ready to be put in place.
/// </summary>
/// <remarks>
/// Separate from <see cref="ApplyUpdate"/> because the two halves are separated by a
/// question: this one is undoable at any point and leaves the install untouched, and the
/// next one is neither. Between them the user gets to say yes to a download that has
/// already succeeded rather than to one that might not.
/// </remarks>
public sealed class StageUpdate(
    ILoggedInUser loggedInUser,
    IUpdateInstaller updateInstaller) : IHandler
{
    /// <param name="Progress">Reports 0..1 while the archive comes down, or null.</param>
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
