using FluentAssertions;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Data;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Startup;
using Helix.Application.Features.Settings.Commands;
using Helix.Domain.Settings;
using Helix.Domain.Users;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SettingsModel = Helix.Domain.Settings.Settings;

namespace Application.UnitTests.Features.Settings;

public sealed class UpdateSettingsTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly ISettingsRepository _settingsRepositoryMock = Substitute.For<ISettingsRepository>();
    private readonly IUnitOfWork _unitOfWorkMock = Substitute.For<IUnitOfWork>();
    private readonly ILoggedInUser _loggedInUserMock = Substitute.For<ILoggedInUser>();
    private readonly IStartupService _startupServiceMock = Substitute.For<IStartupService>();
    private readonly IDesktopService _desktopServiceMock = Substitute.For<IDesktopService>();
    private readonly SettingsModel _settings;

    public UpdateSettingsTests()
    {
        _loggedInUserMock.IsLoggedIn.Returns(true);
        _loggedInUserMock.UserId.Returns(UserId);

        _settings = SettingsModel.Create(UserId, false, false, false, false, 15, Language.English);
        _settingsRepositoryMock.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(_settings);
    }

    private UpdateSettings Handler() => new(
        _settingsRepositoryMock,
        _unitOfWorkMock,
        _loggedInUserMock,
        _startupServiceMock,
        _desktopServiceMock);

    private static UpdateSettings.Request Request(bool setOnStartup, bool setDesktopShortcut, int timerCount = 15) => new(
        AutoConnect: false,
        AutoMinimize: false,
        SetOnStartup: setOnStartup,
        SetDesktopShortcut: setDesktopShortcut,
        TimerCount: timerCount,
        Language: Language.English,
        AuditlogRetentionDays: SettingsModel.DefaultAuditlogRetentionDays,
        StorageAlertThresholdPercent: SettingsModel.DefaultStorageAlertThresholdPercent,
        IdleLockMinutes: SettingsModel.DefaultIdleLockMinutes,
        CloseToTray: SettingsModel.DefaultCloseToTray,
        NotifyOnMinimizeToTray: SettingsModel.DefaultNotifyOnMinimizeToTray);

    [Fact]
    public async Task Handle_Should_SaveWhatTookEffect_WhenOneToggleFails()
    {
        _desktopServiceMock.When(d => d.ToggleDesktopShortcut(true)).Throw(new IOException("denied"));

        Result result = await Handler().Handle(Request(setOnStartup: true, setDesktopShortcut: true, timerCount: 30));

        result.IsFailure.Should().BeTrue();
        _settings.SetOnStartup.Should().BeTrue();
        _settings.SetDesktopShortcut.Should().BeFalse();
        _settings.TimerCount.Should().Be(30);
        await _unitOfWorkMock.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Should_Succeed_WhenBothTogglesApply()
    {
        Result result = await Handler().Handle(Request(setOnStartup: true, setDesktopShortcut: true));

        result.IsSuccess.Should().BeTrue();
        _startupServiceMock.Received(1).ToggleStartup(true);
        _desktopServiceMock.Received(1).ToggleDesktopShortcut(true);
        _settings.SetOnStartup.Should().BeTrue();
        _settings.SetDesktopShortcut.Should().BeTrue();
    }
}
