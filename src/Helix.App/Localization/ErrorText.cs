using Helix.App.Resources.Languages;
using Helix.Domain.Drives;
using System.Globalization;

namespace Helix.App.Localization;

public static class ErrorText
{
    public const string KeyPrefix = "Error_";

    public static string For(Error error)
    {
        string? template = TemplateFor(error);
        if (template is null)
        {
            return error.Description;
        }

        if (error.Arguments.Count == 0)
        {
            return template;
        }

        object?[] arguments = [.. error.Arguments.Select(Render)];

        try
        {
            return string.Format(Culture, template, arguments);
        }
        catch (FormatException)
        {
            return error.Description;
        }
    }

    public static string Line(string letter, Error error) => $"{letter}: {For(error)}";

    public static string KeyFor(string code) => KeyPrefix + code.Replace('.', '_');

    private static CultureInfo Culture => AppResources.Culture ?? CultureInfo.CurrentUICulture;

    private static string? TemplateFor(Error error)
    {
        if (string.IsNullOrEmpty(error.Code))
        {
            return null;
        }

        string key = KeyFor(error.Code);

        return AppResources.ResourceManager.GetString($"{key}_{error.Arguments.Count}", Culture)
            ?? AppResources.ResourceManager.GetString(key, Culture);
    }

    private static object? Render(object? argument) => argument switch
    {
        Error inner => For(inner),
        DriveFailure failure => Line(failure.Letter, failure.Error),
        IEnumerable<DriveFailure> failures => string.Join(Environment.NewLine, failures.Select(f => Line(f.Letter, f.Error))),
        _ => argument,
    };
}
