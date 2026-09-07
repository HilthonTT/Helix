using Helix.Infrastructure.Database;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace Helix.Infrastructure.Cryptography;

public static class PasswordGenerator
{
    private const int PasswordLength = 128;
    private const string PasswordKey = "GeneratedPassword";

    private static ReadOnlySpan<char> ValidChars => "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@$?_-".AsSpan();

    private static string? _cachedPassword;
    private static readonly SemaphoreSlim _initLock = new(1, 1);

    public static async Task InitializeAsync(ILogger? logger = null, CancellationToken cancellationToken = default)
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

            (bool readable, string? existing) = await TryReadFromSecureStorageAsync(logger).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(existing))
            {
                _cachedPassword = existing;
                return;
            }

            if (File.Exists(DatabaseLocation.Path))
            {
                throw new InvalidOperationException(readable
                    ? "Secure storage holds no key for the existing database, so it cannot be opened. " +
                      "The key lives in the Settings folder beside the Data folder; restore it from a backup."
                    : "The database key could not be read from secure storage, so the existing database " +
                      "cannot be opened. Nothing was changed; sign out of Windows and back in, then try again.");
            }

            if (!readable)
            {
                throw new InvalidOperationException(
                    "Secure storage cannot be read, so no database key can be kept safely.");
            }

            logger?.LogInformation("No database key in secure storage; generating one.");

            string fresh = GenerateRandomPassword(PasswordLength);
            await TryWriteToSecureStorageAsync(fresh, logger).ConfigureAwait(false);

            (_, string? persisted) = await TryReadFromSecureStorageAsync(logger).ConfigureAwait(false);
            if (!string.Equals(persisted, fresh, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Failed to persist the database encryption key to SecureStorage; " +
                    "refusing to create a database that could not be reopened.");
            }

            _cachedPassword = fresh;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public static string GetOrCreatePassword()
    {
        return _cachedPassword
            ?? throw new InvalidOperationException(
                $"{nameof(PasswordGenerator)}.{nameof(InitializeAsync)} must be awaited before the database is opened.");
    }

    private static async Task<(bool Readable, string? Key)> TryReadFromSecureStorageAsync(ILogger? logger)
    {
        try
        {
            return (true, await SecureStorage.Default.GetAsync(PasswordKey).ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Reading the database key from secure storage failed.");
            return (false, null);
        }
    }

    private static async Task TryWriteToSecureStorageAsync(string password, ILogger? logger)
    {
        try
        {
            await SecureStorage.Default.SetAsync(PasswordKey, password).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Writing the database key to secure storage failed.");
        }
    }

    internal static string GenerateRandomPassword(int length)
    {
        return new string(RandomNumberGenerator.GetItems<char>(ValidChars, length));
    }

    internal static int ConfiguredPasswordLength => PasswordLength;
}
