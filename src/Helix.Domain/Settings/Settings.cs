using System.Text.Json.Serialization;

namespace Helix.Domain.Settings;

public sealed class Settings : Entity
{
    public const int DefaultTimerCount = 15;

    public const int DefaultAuditlogRetentionDays = 90;

    public const int DefaultStorageAlertThresholdPercent = 10;

    public const int MaximumStorageAlertThresholdPercent = 99;

    public const int DefaultIdleLockMinutes = 0;

    public const bool DefaultCloseToTray = true;

    public const bool DefaultNotifyOnMinimizeToTray = true;

    [JsonConstructor]
    private Settings(
        Guid id,
        Guid userId,
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
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNull(autoConnect, nameof(autoConnect));
        Ensure.NotNull(autoMinimize, nameof(autoMinimize));
        Ensure.NotNull(setOnStartup, nameof(setOnStartup));
        Ensure.NotNull(setDesktopShortcut, nameof(setDesktopShortcut));
        Ensure.MustBePositive(timerCount, nameof(timerCount));
        Ensure.NotNull(language, nameof(language));
        Ensure.MustNotBeNegative(auditlogRetentionDays, nameof(auditlogRetentionDays));
        Ensure.MustNotBeNegative(storageAlertThresholdPercent, nameof(storageAlertThresholdPercent));
        Ensure.MustNotBeNegative(idleLockMinutes, nameof(idleLockMinutes));
        Ensure.NotNull(closeToTray, nameof(closeToTray));
        Ensure.NotNull(notifyOnMinimizeToTray, nameof(notifyOnMinimizeToTray));

        UserId = userId;
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        SetDesktopShortcut = setDesktopShortcut;
        TimerCount = timerCount;
        Language = language;
        AuditlogRetentionDays = auditlogRetentionDays;
        StorageAlertThresholdPercent = storageAlertThresholdPercent;
        IdleLockMinutes = idleLockMinutes;
        CloseToTray = closeToTray;
        NotifyOnMinimizeToTray = notifyOnMinimizeToTray;
    }

    private Settings()
    {
    }

    public Guid UserId { get; private set; }

    public bool AutoConnect { get; private set; }

    public bool AutoMinimize { get; private set; }

    public bool SetOnStartup { get; private set; }

    public bool SetDesktopShortcut { get; private set; }

    public int TimerCount { get; private set; }

    public Language Language { get; private set; }

    public int AuditlogRetentionDays { get; private set; }

    public int StorageAlertThresholdPercent { get; private set; }

    public int IdleLockMinutes { get; private set; }

    public bool CloseToTray { get; private set; }

    public bool NotifyOnMinimizeToTray { get; private set; }

    public static Settings Create(
        Guid userId,
        bool autoConnect,
        bool autoMinimize,
        bool setOnStartup,
        bool setDesktopShortcut,
        int timerCount,
        Language language,
        int auditlogRetentionDays = DefaultAuditlogRetentionDays,
        int storageAlertThresholdPercent = DefaultStorageAlertThresholdPercent,
        int idleLockMinutes = DefaultIdleLockMinutes,
        bool closeToTray = DefaultCloseToTray,
        bool notifyOnMinimizeToTray = DefaultNotifyOnMinimizeToTray)
    {
        var settings = new Settings(
            Guid.CreateVersion7(),
            userId,
            autoConnect,
            autoMinimize,
            setOnStartup,
            setDesktopShortcut,
            timerCount,
            language,
            auditlogRetentionDays,
            storageAlertThresholdPercent,
            idleLockMinutes,
            closeToTray,
            notifyOnMinimizeToTray);

        return settings;
    }

    public void Update(
        bool autoConnect,
        bool autoMinimize,
        bool setOnStartup,
        bool setDesktopShorcut,
        int timerCount,
        Language language,
        int auditlogRetentionDays,
        int storageAlertThresholdPercent,
        int idleLockMinutes,
        bool closeToTray,
        bool notifyOnMinimizeToTray)
    {
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        SetDesktopShortcut = setDesktopShorcut;
        TimerCount = timerCount;
        Language = language;
        AuditlogRetentionDays = auditlogRetentionDays;
        StorageAlertThresholdPercent = storageAlertThresholdPercent;
        IdleLockMinutes = idleLockMinutes;
        CloseToTray = closeToTray;
        NotifyOnMinimizeToTray = notifyOnMinimizeToTray;
    }
}
