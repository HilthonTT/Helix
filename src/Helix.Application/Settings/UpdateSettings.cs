using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Caching;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Startup;
using Helix.Application.Core.Extensions;
using Helix.Domain.Settings;
using SharedKernel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Settings;

public sealed class UpdateSettings(
    IDbContext context, 
    ILoggedInUser loggedInUser, 
    IStartupService startupService,
    IDesktopService desktopService,
    ICacheService cacheService) : IHandler
{
    public sealed record Request(
        bool AutoConnect, 
        bool AutoMinimize, 
        bool SetOnStartup, 
        bool SetDesktopShortcut,
        int TimerCount, 
        Language Language)
    {
        public sealed class Builder(
            bool autoConnect,
            bool autoMinimize,
            bool setOnStartup,
            bool setDesktopShortcut,
            int timerCount,
            Language language)
        {
            public bool AutoConnect { get; set; } = autoConnect;

            public bool AutoMinimize { get; set; } = autoMinimize;

            public bool SetOnStartup { get; set; } = setOnStartup;

            public bool SetDesktopShortcut { get; set; } = setDesktopShortcut;

            public int TimerCount { get; set; } = timerCount;

            public Language Language { get; set; } = language;

            public Request Build() =>
                new(AutoConnect, AutoMinimize, SetOnStartup, SetDesktopShortcut, TimerCount, Language);
        }
    }

    public async Task<Result> Handle(Request request, CancellationToken cancellationToken = default)
    {
        Result validationResult = Validate(request);
        if (validationResult.IsFailure)
        {
            return validationResult;
        }

        SettingsModel? settings = await context.Settings.GetByUserIdAsAsync(loggedInUser.UserId, cancellationToken);
        if (settings is null)
        {
            return Result.Failure(SettingsError.NotFound);
        }

        settings.Update(
            request.AutoConnect,
            request.AutoMinimize, 
            request.SetOnStartup,
            request.SetDesktopShortcut,
            request.TimerCount,
            request.Language);

        startupService.ToggleStartup(settings.SetOnStartup);

        desktopService.ToggleDesktopShortcut(settings.SetDesktopShortcut);

        await context.SaveChangesAsync(cancellationToken);

        string cacheKey = CacheKeys.Settings.GetByUserId(loggedInUser.UserId);

        await cacheService.RemoveAsync(cacheKey, cancellationToken);

        return Result.Success();
    }

    private static Result Validate(Request request)
    {
        if (request.TimerCount < 0)
        {
            return Result.Failure(SettingsError.TimerCountMustBePositive);
        }

        return Result.Success();
    }
}
