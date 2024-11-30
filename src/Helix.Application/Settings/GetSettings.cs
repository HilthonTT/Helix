using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Domain.Users;
using SharedKernel;
using Helix.Domain.Settings;
using SettingsModel = Helix.Domain.Settings.Settings;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Abstractions.Caching;

namespace Helix.Application.Settings;

public sealed class GetSettings(
    ISettingsRepository settingsRepository, 
    IUnitOfWork unitOfWork,
    ILoggedInUser loggedInUser, 
    ICacheService cacheService) : IHandler
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

            string cacheKey = CacheKeys.Settings.GetByUserId(loggedInUser.UserId);

            SettingsModel? settings = await cacheService.GetAsync<SettingsModel>(cacheKey, cancellationToken);
            if (settings is not null)
            {
                return settings;
            }

            settings = await GetOrCreateSettingsAsync(cancellationToken);
            if (settings is null)
            {
                return Result.Failure<SettingsModel>(Error.NullValue);
            }

            await cacheService.SetAsync(cacheKey, settings, cancellationToken: cancellationToken);

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
        var settings = SettingsModel.Create(loggedInUser.UserId, false, false, false, false, 20, Language.English);

        settingsRepository.Insert(settings);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return settings.Id;
    }
}
