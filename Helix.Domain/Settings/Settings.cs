using SharedKernel;

namespace Helix.Domain.Settings;

public sealed class Settings : Entity
{
    public const int DefaultTimerCount = 15;

    private Settings(
        Guid id,
        Guid userId,
        bool autoConnect,
        bool autoMinimize,
        bool setOnStartup,
        int timerCount,
        Language language) 
        : base(id)
    {
        Ensure.NotNullOrEmpty(id, nameof(id));
        Ensure.NotNullOrEmpty(userId, nameof(userId));
        Ensure.NotNull(autoConnect, nameof(autoConnect));
        Ensure.NotNull(autoMinimize, nameof(autoMinimize));
        Ensure.NotNull(setOnStartup, nameof(setOnStartup));
        Ensure.MustBePositive(timerCount, nameof(timerCount));
        Ensure.NotNull(language, nameof(language));

        UserId = userId;
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        TimerCount = timerCount;
        Language = language;
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

    public int TimerCount { get; private set; }

    public Language Language { get; private set; }

    public static Settings Create(
        Guid userId, 
        bool autoConnect, 
        bool autoMinimize, 
        bool setOnStartup,
        int timerCount, 
        Language language)
    {
        var settings = new Settings(
            Guid.NewGuid(),
            userId,
            autoConnect,
            autoMinimize,
            setOnStartup,
            timerCount,
            language);

        return settings;
    }

    public void Update(bool autoConnect, bool autoMinimize, bool setOnStartup, int timerCount, Language language)
    {
        AutoConnect = autoConnect;
        AutoMinimize = autoMinimize;
        SetOnStartup = setOnStartup;
        TimerCount = timerCount;
        Language = language;
    }
}
