using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Auditlogs;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Auditlogs.Commands;

/// <summary>
/// Drops audit entries older than the user's retention setting.
/// </summary>
/// <remarks>
/// Nothing removed audit entries before this: the table only ever grew, a few rows per
/// drive event, for as long as the install lived. Run once per sign-in rather than on a
/// timer — the log is only read from the audit page, so trimming it more often than the
/// user can look at it buys nothing.
///
/// A retention of zero means keep everything, and is honoured by doing nothing at all.
/// </remarks>
public sealed class PruneAuditlogs(
    IAuditlogRepository auditlogRepository,
    ISettingsRepository settingsRepository,
    ILoggedInUser loggedInUser,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    /// <returns>How many entries were removed.</returns>
    public async Task<Result<int>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<int>(AuthenticationErrors.InvalidPermissions);
        }

        SettingsModel? settings = await settingsRepository.GetByUserIdAsync(loggedInUser.UserId, cancellationToken);
        if (settings is null)
        {
            return Result.Failure<int>(SettingsErrors.NotFound);
        }

        if (settings.AuditlogRetentionDays <= 0)
        {
            return 0;
        }

        DateTime cutoff = dateTimeProvider.UtcNow.AddDays(-settings.AuditlogRetentionDays);

        int removed = await auditlogRepository.DeleteOlderThanAsync(
            loggedInUser.UserId,
            cutoff,
            cancellationToken);

        return removed;
    }
}
