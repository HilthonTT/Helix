using Helix.Domain.Settings;
using System.Globalization;

namespace Helix.App.Helpers;

public static class CultureSwitcher
{
    private static readonly Dictionary<Language, string> LanguageToCultureMap = new()
    {
        { Language.English, "en" },
        { Language.French, "fr" },
        { Language.German, "de" },
        { Language.Indonesian, "id" },
        { Language.Japanese, "ja" },
        { Language.Dutch, "nl" }
    };

    public static void SwitchCulture(Language language)
    {
        if (LanguageToCultureMap.TryGetValue(language, out string? cultureCode))
        {
            var switchToCulture = new CultureInfo(cultureCode);
            LocalizationResourceManager.Instance.SetCulture(switchToCulture);
        }
        else
        {
            var defaultCulture = new CultureInfo("en");

            LocalizationResourceManager.Instance.SetCulture(defaultCulture);
        }
    }
}
