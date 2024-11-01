using Helix.Domain.Settings;

namespace Helix.App.Helpers;

internal sealed class LanguageService
{
    private Language _currentLanguage;

    public event Action<Language>? LanguageChanged;

    public static LanguageService Instance { get; } = new();

    public Language CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                CultureSwitcher.SwitchCulture(_currentLanguage);
                LanguageChanged?.Invoke(_currentLanguage);
            }
        }
    }

    private LanguageService()
    {
        _currentLanguage = Language.English;
    }
}