using System.Security.Cryptography;
using System.Text;

namespace Helix.Infrastructure.Cryptography;

public static class PasswordGenerator
{
    private const int PasswordLength = 128;

    private const string PasswordKey = "GeneratedPassword";

    public static string GetOrCreatePassword()
    {
        // Retrieve the stored password if it exists
        string? storedPassword = GetPasswordFromSecureStorage();

        if (!string.IsNullOrWhiteSpace(storedPassword))
        {
            return storedPassword;
        }

        string newPassword = GenerateRandomPassword(PasswordLength);

        StorePasswordInSecureStorage(newPassword);

        return newPassword;
    }

    private static string GenerateRandomPassword(int length)
    {
        const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@$?_-";

        var passwordBuilder = new StringBuilder();

        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            byte[] randomBytes = new byte[length];
            rng.GetBytes(randomBytes);

            for (int i = 0; i < length; i++)
            {
                passwordBuilder.Append(validChars[randomBytes[i] % validChars.Length]);
            }
        }

        return passwordBuilder.ToString();
    }

    private static string? GetPasswordFromSecureStorage()
    {
        try
        {
            // Synchronous handling by using Task.Run and .Result to block execution
            return Task.Run(() => SecureStorage.Default.GetAsync(PasswordKey)).Result;
        }
        catch (Exception ex)
        {
            // Handle any exceptions, e.g., if SecureStorage is unavailable
            Console.WriteLine($"Error retrieving password: {ex.Message}");
            return null;
        }
    }

    private static void StorePasswordInSecureStorage(string password)
    {
        try
        {
            // Synchronous handling by using Task.Run and .Wait to block execution
            Task.Run(() => SecureStorage.Default.SetAsync(PasswordKey, password)).Wait();
        }
        catch (Exception ex)
        {
            // Handle any exceptions, e.g., if SecureStorage is unavailable
            Console.WriteLine($"Error storing password: {ex.Message}");
        }
    }
}
