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

    /// <summary>
    /// Eagerly resolves the SQLCipher key from <see cref="SecureStorage"/>, generating
    /// and persisting a fresh one if none exists. Safe to call multiple times; subsequent
    /// calls are no-ops. Must be awaited before <see cref="GetOrCreatePassword"/> is read.
    /// </summary>
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

            // Never generate over a database that already exists. A key that cannot be
            // read is not the same as no key: a DPAPI hiccup at logon used to be
            // answered by generating a fresh one and writing it over the real one, at
            // which point the database was unreadable for good - even after the hiccup
            // cleared. Refusing here costs a start-up that fails with a message; the
            // alternative cost every drive the user had.
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

            // Information, not Debug: a fresh key on a machine that already has a
            // database is the one event that makes that database unreadable, and the
            // log the user is asked to send has to be able to say it happened.
            logger?.LogInformation("No database key in secure storage; generating one.");

            string fresh = GenerateRandomPassword(PasswordLength);
            await TryWriteToSecureStorageAsync(fresh, logger).ConfigureAwait(false);

            // If the key was not actually persisted, refuse to continue: creating the
            // database with an in-memory-only key means it can never be reopened after
            // a restart — silent, unrecoverable loss of all user data.
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

    /// <summary>
    /// Reads the key, telling "there is none" apart from "it could not be read": the
    /// first is a fresh install, the second is a store that must not be written to.
    /// </summary>
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

    /// <summary>
    /// Internal hook for unit tests — generates a fresh random password without
    /// touching <see cref="SecureStorage"/> or the cached value.
    /// </summary>
    internal static string GenerateRandomPassword(int length)
    {
        // GetItems performs unbiased sampling — `randomByte % ValidChars.Length`
        // would skew the distribution toward the start of the alphabet because
        // 256 is not a multiple of the character-set size. Only affects freshly
        // generated keys; existing keys are read back from SecureStorage as-is.
        return new string(RandomNumberGenerator.GetItems<char>(ValidChars, length));
    }

    /// <summary>The configured password length, exposed for tests.</summary>
    internal static int ConfiguredPasswordLength => PasswordLength;
}
