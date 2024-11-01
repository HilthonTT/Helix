using Helix.App.Resources.Languages;
using Helix.Domain.Settings;
using System.Globalization;

namespace Helix.App.Helpers;

public static class CultureSwitcher
{
    public static readonly string[] Languages =
    [
        "English", 
        "Français", 
        "Deutsch", 
        "Bahasa Indonesia", 
        "日本語", 
        "Nederlands"
    ];

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

    public static Language GetCurrentLanguage()
    {
        string currentCulture = AppResources.Culture.Name;

        return LanguageToCultureMap.FirstOrDefault(x => x.Value.Contains(currentCulture)).Key;
    }

    public static Language StringToLanguage(string languageString)
    {
        return languageString switch
        {
            "English" => Language.English,
            "Français" => Language.French,
            "Deutsch" => Language.German,
            "Bahasa Indonesia" => Language.Indonesian,
            "日本語" => Language.Japanese,
            "Nederlands" => Language.Dutch,
            _ => throw new ArgumentException($"Unknown language: {languageString}")
        };
    }

    public static string LanguageToString(Language language)
    {
        return language switch
        {
            Language.English => "English",
            Language.French => "Français",
            Language.German => "Deutsch",
            Language.Indonesian => "Bahasa Indonesia",
            Language.Japanese => "日本語",
            Language.Dutch => "Nederlands",
            _ => "Unknown Language"
        };
    }
}
