using Helix.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Helix.Application.Core.Extensions;

public static class UserDbSetExtensions
{
    public static async Task<bool> IsUsernameUniqueAsync(
        this DbSet<User> users,
        string username, 
        CancellationToken cancellationToken = default)
    {
        return !await users.AnyAsync(u => u.Username == username, cancellationToken);
    }
}
