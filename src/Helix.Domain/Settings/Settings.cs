using System.Text.Json.Serialization;

namespace Helix.Domain.Settings;

public sealed class Settings : Entity
{
    public const int DefaultTimerCount = 15;

    /// <summary>
    /// How long audit entries are kept, in days. Zero keeps them forever.
    /// </summary>
    /// <remarks>
    /// The log grows a few rows per drive event and nothing ever removed them, so an
    /// install left running for a year accumulated without bound. Ninety days is long
    /// enough to answer "when did this start happening?" and short enough that the table
    /// stays a log rather than an archive.
    /// </remarks>
    public const int DefaultAuditlogRetentionDays = 90;

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
        int auditlogRetentionDays)
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

        UserId = userId;
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        SetDesktopShortcut = setDesktopShortcut;
        TimerCount = timerCount;
        Language = language;
        AuditlogRetentionDays = auditlogRetentionDays;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Settings"/> class.
    /// </summary>
    /// <remarks>
    /// Required by EF Core.
    /// </remarks>
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

    /// <summary>
    /// Days of audit history to keep. Zero keeps everything — see
    /// <see cref="DefaultAuditlogRetentionDays"/>.
    /// </summary>
    public int AuditlogRetentionDays { get; private set; }

    public static Settings Create(
        Guid userId,
        bool autoConnect,
        bool autoMinimize,
        bool setOnStartup,
        bool setDesktopShortcut,
        int timerCount,
        Language language,
        int auditlogRetentionDays = DefaultAuditlogRetentionDays)
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
            auditlogRetentionDays);

        return settings;
    }

    public void Update(
        bool autoConnect,
        bool autoMinimize,
        bool setOnStartup,
        bool setDesktopShorcut,
        int timerCount,
        Language language,
        int auditlogRetentionDays)
    {
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        SetDesktopShortcut = setDesktopShorcut;
        TimerCount = timerCount;
        Language = language;
        AuditlogRetentionDays = auditlogRetentionDays;
    }
}
