namespace Helix.Domain.Users;

public interface IUserRepository
{
    Task<bool> IsUsernameUniqueAsync(string username, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    void Insert(User user);

    void Remove(User user);
}
