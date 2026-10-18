using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Users.Commands;

public sealed class RegisterUser(
    IUserRepository userRepository,
    ISettingsRepository settingsRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IRecoveryKeyGenerator recoveryKeyGenerator,
    ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(
        string Username,
        string Password,
        string ConfirmedPassword,
        Language Language = Language.English);

    public sealed record Response(User User, string RecoveryKey);

    public async Task<Result<Response>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<Response>(validationResult.Error);
        }

        if (request.Password != request.ConfirmedPassword)
        {
            return Result.Failure<Response>(AuthenticationErrors.PasswordsDoNotMatch);
        }

        string username = request.Username.Trim();

        if (!await userRepository.IsUsernameUniqueAsync(username, cancellationToken))
        {
            return Result.Failure<Response>(AuthenticationErrors.UsernameNotUnique);
        }

        string passwordHash = passwordHasher.Hash(request.Password);

        var user = User.Create(username, passwordHash);

        string recoveryKey = recoveryKeyGenerator.Generate();
        user.SetRecoveryKey(passwordHasher.Hash(recoveryKey));

        userRepository.Insert(user);

        // The language picked on the register page is the account's from the start; GetSettings
        // would otherwise create the row in English on the first sign-in and switch the UI back.
        settingsRepository.Insert(SettingsModel.Create(
            user.Id, false, false, false, false, SettingsModel.DefaultTimerCount, request.Language));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure<Response>(AuthenticationErrors.UsernameNotUnique);
        }

        loggedInUser.Login(user.Id, user.Username);

        return new Response(user, recoveryKey);
    }

    private static Result Validate(Request request)
    {
        string[] properties = [request.Username, request.Password, request.ConfirmedPassword];

        return properties.Any(string.IsNullOrWhiteSpace)
             ? Result.Failure(ValidationErrors.MissingFields)
             : Result.Success();
    }
}
