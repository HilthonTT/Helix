namespace Helix.Application.Abstractions.Cryptography;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string passwordHash);
}
