using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Cryptography;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Helix.Application.Users;

public sealed class RegisterUser(IDbContext context, IPasswordHasher passwordHasher, ILoggedInUser loggedInUser)
{
    public sealed record Request(string Username, string Password);

    public async Task<User> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (await context.Users.AnyAsync(u => u.Username == request.Username, cancellationToken))
        {
            throw new Exception("The username is already in use");
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
            throw new Exception("The username is already in use");
        }

        loggedInUser.Login(user.Id, user.Username);

        return user;
    }
}
