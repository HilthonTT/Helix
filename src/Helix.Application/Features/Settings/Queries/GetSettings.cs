using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Handlers;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Helix.Application.Features.Settings.Queries;

public sealed class GetSettings(
    ISettingsRepository settingsRepository, 
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser) : IHandler
{
    private static readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task<Result<SettingsModel>> Handle(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (!loggedInUser.IsLoggedIn)
            {
                return Result.Failure<SettingsModel>(AuthenticationErrors.InvalidPermissions);
            }

            var settings = await GetOrCreateSettingsAsync(cancellationToken);
            if (settings is null)
            {
                return Result.Failure<SettingsModel>(Error.NullValue);
            }

            return settings;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<SettingsModel?> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        SettingsModel? settings = await settingsRepository.GetByUserIdAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        Guid settingsId = await CreateSettingsAsync(cancellationToken);

        return await settingsRepository.GetByIdAsNoTrackingAsync(settingsId, cancellationToken);
    }

    private async Task<Guid> CreateSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = SettingsModel.Create(
            loggedInUser.UserId, false, false, false, false, SettingsModel.DefaultTimerCount, Language.English);

        settingsRepository.Insert(settings);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return settings.Id;
    }
}
