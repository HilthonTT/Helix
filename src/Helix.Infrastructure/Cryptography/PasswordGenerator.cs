using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Helix.Infrastructure.Cryptography;

public static class PasswordGenerator
{
    private const int PasswordLength = 128;
    private const string PasswordKey = "GeneratedPassword";

    private static ReadOnlySpan<char> ValidChars => "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@$?_-".AsSpan();

    private static string? _cachedPassword;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    /// <summary>
    /// Eagerly resolves the SQLCipher key from <see cref="SecureStorage"/>, generating
    /// and persisting a fresh one if none exists. Safe to call multiple times; subsequent
    /// calls are no-ops. Must be awaited before <see cref="GetOrCreatePassword"/> is read.
    /// </summary>
    public static async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedPassword is not null)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cachedPassword is not null)
            {
                return;
            }

            string? existing = await TryReadFromSecureStorageAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _cachedPassword = existing;
                return;
            }

            string fresh = GenerateRandomPassword(PasswordLength);
            await TryWriteToSecureStorageAsync(fresh).ConfigureAwait(false);
            _cachedPassword = fresh;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <summary>
    /// Returns the cached SQLCipher key. Throws if <see cref="InitializeAsync"/>
    /// has not been awaited.
    /// </summary>
    public static string GetOrCreatePassword()
    {
        return _cachedPassword
            ?? throw new InvalidOperationException(
                $"{nameof(PasswordGenerator)}.{nameof(InitializeAsync)} must be awaited before the database is opened.");
    }

    private static async Task<string?> TryReadFromSecureStorageAsync()
    {
        try
        {
            return await SecureStorage.Default.GetAsync(PasswordKey).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Helix: SecureStorage read failed ({ex.GetType().Name}).");
            return null;
        }
    }

    private static async Task TryWriteToSecureStorageAsync(string password)
    {
        try
        {
            await SecureStorage.Default.SetAsync(PasswordKey, password).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Helix: SecureStorage write failed ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Internal hook for unit tests — generates a fresh random password without
    /// touching <see cref="SecureStorage"/> or the cached value.
    /// </summary>
    internal static string GenerateRandomPassword(int length)
    {
        var passwordBuilder = new StringBuilder(length);

        byte[] randomBytes = new byte[length];
        RandomNumberGenerator.Fill(randomBytes);

        for (int i = 0; i < length; i++)
        {
            char randomChar = ValidChars[randomBytes[i] % ValidChars.Length];
            passwordBuilder.Append(randomChar);
        }

        return passwordBuilder.ToString();
    }

    /// <summary>The configured password length, exposed for tests.</summary>
    internal static int ConfiguredPasswordLength => PasswordLength;
}
