using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Extensions;
using Helix.Domain.Settings;
using SharedKernel;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Settings;

public sealed class UpdateSettings(IDbContext context, ILoggedInUser loggedInUser) : IHandler
{
    public sealed record Request(bool AutoConnect, bool AutoMinimize, bool SetOnStartup, int TimerCount, Language Language)
    {
        public sealed class Builder
        {
            public bool AutoConnect { get; set; }

            public bool AutoMinimize { get; set; }

            public bool SetOnStartup { get; set; }

            public int TimerCount { get; set; }

            public Language Language { get; set; }

            public Builder(bool autoConnect, bool autoMinimize, bool setOnStartup, int timerCount, Language language)
            {
                AutoConnect = autoConnect;
                AutoMinimize = autoMinimize;
                SetOnStartup = setOnStartup;
                TimerCount = timerCount;
                Language = language;
            }

            public Request Build() =>
                new(AutoConnect, AutoMinimize, SetOnStartup, TimerCount, Language);
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

        settings.Update(request.AutoMinimize, request.AutoMinimize, request.SetOnStartup, request.TimerCount, request.Language);

        await context.SaveChangesAsync(cancellationToken);

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
