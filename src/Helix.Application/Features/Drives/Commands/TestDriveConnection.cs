using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Application.Core.Validation;
using Helix.Domain.Drives;
using Helix.Domain.Users;

namespace Helix.Application.Features.Drives.Commands;

/// <summary>
/// Checks a set of connection details against the server without saving them.
/// </summary>
/// <remarks>
/// Takes the form's own fields rather than a drive id, because the whole point is to
/// answer the question before there is anything to load: a wrong password used to be
/// discovered at the first connect, long after the modal was closed, and the error came
/// back with no obvious link to the field that caused it.
///
/// The drive built here is never inserted and never reaches the repository — it exists
/// only to carry the values into <see cref="INasConnector.TestAsync"/>, which is defined
/// not to mount anything or claim the letter.
/// </remarks>
public sealed class TestDriveConnection(
    ILoggedInUser loggedInUser,
    INasConnector nasConnector) : IHandler
{
    public sealed record Request(string Letter, string Host, string Name, string Username, string Password);

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        var candidate = Drive.Create(
            loggedInUser.UserId,
            request.Letter,
            request.Host,
            request.Name,
            request.Username,
            request.Password);

        return await nasConnector.TestAsync(candidate, cancellationToken);
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Letter) || request.Letter.Length != 1 || !char.IsLetter(request.Letter[0]))
        {
            return Result.Failure(DriveErrors.NotALetter);
        }

        if (!GeneralValidation.IsValidHost(request.Host))
        {
            return Result.Failure(ValidationErrors.InvalidHost);
        }

        string[] properties = [request.Letter, request.Host, request.Name, request.Username, request.Password];

        return properties.Any(string.IsNullOrWhiteSpace)
            ? Result.Failure(ValidationErrors.MissingFields)
            : Result.Success();
    }
}
