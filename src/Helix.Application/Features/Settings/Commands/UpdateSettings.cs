using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Startup;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Settings.Commands;

public sealed class UpdateSettings(
    ISettingsRepository settingsRepository,
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser,
    IStartupService startupService,
    IDesktopService desktopService) : IHandler
{
    public sealed record Request(
        bool AutoConnect,
        bool AutoMinimize,
        bool SetOnStartup,
        bool SetDesktopShortcut,
        int TimerCount,
        Language Language,
        int AuditlogRetentionDays,
        int StorageAlertThresholdPercent,
        int IdleLockMinutes,
        bool CloseToTray,
        bool NotifyOnMinimizeToTray)
    {
        public sealed class Builder(
            bool autoConnect,
            bool autoMinimize,
            bool setOnStartup,
            bool setDesktopShortcut,
            int timerCount,
            Language language,
            int auditlogRetentionDays,
            int storageAlertThresholdPercent,
            int idleLockMinutes,
            bool closeToTray,
            bool notifyOnMinimizeToTray)
        {
            public bool AutoConnect { get; set; } = autoConnect;

            public bool AutoMinimize { get; set; } = autoMinimize;

            public bool SetOnStartup { get; set; } = setOnStartup;

            public bool SetDesktopShortcut { get; set; } = setDesktopShortcut;

            public int TimerCount { get; set; } = timerCount;

            public Language Language { get; set; } = language;

            public int AuditlogRetentionDays { get; set; } = auditlogRetentionDays;

            public int StorageAlertThresholdPercent { get; set; } = storageAlertThresholdPercent;

            public int IdleLockMinutes { get; set; } = idleLockMinutes;

            public bool CloseToTray { get; set; } = closeToTray;

            public bool NotifyOnMinimizeToTray { get; set; } = notifyOnMinimizeToTray;

            public Request Build() => new(
                AutoConnect,
                AutoMinimize,
                SetOnStartup,
                SetDesktopShortcut,
                TimerCount,
                Language,
                AuditlogRetentionDays,
                StorageAlertThresholdPercent,
                IdleLockMinutes,
                CloseToTray,
                NotifyOnMinimizeToTray);
        }
    }

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure(AuthenticationErrors.InvalidPermissions);
        }

        SettingsModel? settings = await settingsRepository.GetByUserIdAsync(loggedInUser.UserId, cancellationToken);
        if (settings is null)
        {
            return Result.Failure(SettingsErrors.NotFound);
        }

        bool startupChanged = settings.SetOnStartup != request.SetOnStartup;
        bool desktopChanged = settings.SetDesktopShortcut != request.SetDesktopShortcut;

        settings.Update(
            request.AutoConnect,
            request.AutoMinimize,
            request.SetOnStartup,
            request.SetDesktopShortcut,
            request.TimerCount,
            request.Language,
            request.AuditlogRetentionDays,
            request.StorageAlertThresholdPercent,
            request.IdleLockMinutes,
            request.CloseToTray,
            request.NotifyOnMinimizeToTray);

        try
        {
            if (startupChanged)
            {
                startupService.ToggleStartup(settings.SetOnStartup);
            }

            if (desktopChanged)
            {
                desktopService.ToggleDesktopShortcut(settings.SetDesktopShortcut);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return Result.Failure(SettingsErrors.ShortcutUpdateFailed(ex.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (request.TimerCount <= 0)
        {
            return Result.Failure(SettingsErrors.TimerCountMustBePositive);
        }

        if (request.AuditlogRetentionDays < 0)
        {
            return Result.Failure(SettingsErrors.RetentionMustNotBeNegative);
        }

        if (request.StorageAlertThresholdPercent < 0 ||
            request.StorageAlertThresholdPercent > SettingsModel.MaximumStorageAlertThresholdPercent)
        {
            return Result.Failure(SettingsErrors.StorageAlertThresholdOutOfRange);
        }

        if (request.IdleLockMinutes < 0)
        {
            return Result.Failure(SettingsErrors.IdleLockMustNotBeNegative);
        }

        return Result.Success();
    }
}
