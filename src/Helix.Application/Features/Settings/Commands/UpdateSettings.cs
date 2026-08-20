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
        int AuditlogRetentionDays)
    {
        public sealed class Builder(
            bool autoConnect,
            bool autoMinimize,
            bool setOnStartup,
            bool setDesktopShortcut,
            int timerCount,
            Language language,
            int auditlogRetentionDays)
        {
            public bool AutoConnect { get; set; } = autoConnect;

            public bool AutoMinimize { get; set; } = autoMinimize;

            public bool SetOnStartup { get; set; } = setOnStartup;

            public bool SetDesktopShortcut { get; set; } = setDesktopShortcut;

            public int TimerCount { get; set; } = timerCount;

            public Language Language { get; set; } = language;

            public int AuditlogRetentionDays { get; set; } = auditlogRetentionDays;

            public Request Build() => new(
                AutoConnect,
                AutoMinimize,
                SetOnStartup,
                SetDesktopShortcut,
                TimerCount,
                Language,
                AuditlogRetentionDays);
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

        settings.Update(
            request.AutoConnect,
            request.AutoMinimize,
            request.SetOnStartup,
            request.SetDesktopShortcut,
            request.TimerCount,
            request.Language,
            request.AuditlogRetentionDays);

        // The shortcut services throw IOException on failure (e.g. the startup folder
        // is locked down by policy). Handlers must never throw for expected failures —
        // convert to a Result so the settings page shows an alert instead of crashing.
        try
        {
            startupService.ToggleStartup(settings.SetOnStartup);

            desktopService.ToggleDesktopShortcut(settings.SetDesktopShortcut);
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

        // Zero is legal and means "keep everything"; negative is not a shorter retention,
        // it is a typo that would delete entries dated in the future.
        if (request.AuditlogRetentionDays < 0)
        {
            return Result.Failure(SettingsErrors.RetentionMustNotBeNegative);
        }

        return Result.Success();
    }
}
