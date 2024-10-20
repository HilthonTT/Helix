using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace Helix.Application.Users;

public sealed class RegisterUser(IDbContext context, IPasswordHasher passwordHasher, ILoggedInUser loggedInUser)
{
    public sealed record Request(string Username, string Password, string ConfirmedPassword);

    public async Task<Result<User>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return Result.Failure<User>(validationResult.Error);
        }

        if (request.Password != request.ConfirmedPassword)
        {
            return Result.Failure<User>(AuthenticationErrors.PasswordsDoNotMatch);
        }

        if (await context.Users.AnyAsync(u => u.Username == request.Username, cancellationToken))
        {
            return Result.Failure<User>(AuthenticationErrors.UsernameNotUnique);
        }

        string passwordHash = passwordHasher.Hash(request.Password);

        var user = User.Create(request.Username, passwordHash);

        context.Users.Add(user);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            return Result.Failure<User>(AuthenticationErrors.UsernameNotUnique);
        }

        loggedInUser.Login(user.Id, user.Username);

        return user;
    }

    private static Result Validate(Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
        {
            return Result.Failure(Error.NullValue);
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure(Error.NullValue);
        }

        if (string.IsNullOrWhiteSpace(request.ConfirmedPassword))
        {
            return Result.Failure(Error.NullValue);
        }

        return Result.Success();
    }
}
