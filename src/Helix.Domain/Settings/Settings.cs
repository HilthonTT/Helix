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

    /// <summary>
    /// Free space, as a percentage of a volume, below which Helix says something. Zero
    /// turns the warning off.
    /// </summary>
    /// <remarks>
    /// Ten percent because that is roughly where a NAS stops behaving well rather than
    /// where it stops accepting writes — snapshots, parity and the filesystem's own
    /// housekeeping all want room — and because the whole point is to be told before the
    /// backup that fails is the one you needed.
    /// </remarks>
    public const int DefaultStorageAlertThresholdPercent = 10;

    /// <summary>The largest threshold that still means anything; see the constant above.</summary>
    public const int MaximumStorageAlertThresholdPercent = 99;

    /// <summary>
    /// Minutes of no input after which Helix asks for the password again. Zero never
    /// locks.
    /// </summary>
    /// <remarks>
    /// Off by default, including for a new account. Everything Helix guards is already
    /// behind an encrypted database and a sign-in; the lock is for the machine someone
    /// else can walk up to, which is a thing the user knows about their own desk and
    /// Helix does not. A NAS tool that surprises people by locking is a NAS tool people
    /// turn off.
    /// </remarks>
    public const int DefaultIdleLockMinutes = 0;

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
        int idleLockMinutes)
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

    /// <summary>
    /// Free space below which a volume is reported as running out, as a percentage of
    /// its size. Zero means never — see <see cref="DefaultStorageAlertThresholdPercent"/>.
    /// </summary>
    /// <remarks>
    /// A percentage rather than a byte figure because one Helix install commonly watches
    /// volumes orders of magnitude apart in size, and 50 GB left is comfortable on a 43 TB
    /// pool and nearly full on a 64 GB one.
    /// </remarks>
    public int StorageAlertThresholdPercent { get; private set; }

    /// <summary>
    /// Minutes of no input before the app locks itself; 0 never locks — see
    /// <see cref="DefaultIdleLockMinutes"/>.
    /// </summary>
    /// <remarks>
    /// A lock, not a sign-out: the drives stay mounted and the watchdog keeps
    /// reconnecting them behind it. An unattended tool that stopped doing its job the
    /// moment nobody was at the keyboard would be a tool that only works while watched.
    /// </remarks>
    public int IdleLockMinutes { get; private set; }

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
        int idleLockMinutes = DefaultIdleLockMinutes)
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
            idleLockMinutes);

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
        int idleLockMinutes)
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
    }
}
