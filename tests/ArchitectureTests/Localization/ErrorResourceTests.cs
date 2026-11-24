using FluentAssertions;
using Helix.Application;
using Helix.Domain;
using Helix.Domain.Drives;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ArchitectureTests.Localization;

public sealed partial class ErrorResourceTests
{
    private const string KeyPrefix = "Error_";

    private static readonly string[] Cultures = ["", "de", "fr", "id", "ja", "nl"];

    private static readonly HashSet<string> PassThroughCodes = ["Mount.System", "Drive.MountNotOpened"];

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Resources = new(LoadResources);

    [Fact]
    public void EveryCulture_Should_HaveTheSameKeys()
    {
        HashSet<string> english = [.. Resources.Value[""].Keys];

        foreach (string culture in Cultures.Skip(1))
        {
            HashSet<string> translated = [.. Resources.Value[culture].Keys];

            english.Except(translated).Should().BeEmpty($"'{culture}' should translate every English key");
            translated.Except(english).Should().BeEmpty($"'{culture}' should have no key English lacks");
        }
    }

    [Fact]
    public void EveryCulture_Should_KeepThePlaceholdersOfEachKey()
    {
        List<string> mismatches = [];

        foreach ((string key, string english) in Resources.Value[""])
        {
            string expected = Placeholders(english);

            foreach (string culture in Cultures.Skip(1))
            {
                if (Resources.Value[culture].TryGetValue(key, out string? value) && Placeholders(value) != expected)
                {
                    mismatches.Add($"{culture}:{key}");
                }
            }
        }

        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void EveryErrorCode_Should_HaveATranslation_ThatUsesOnlyItsArguments()
    {
        Dictionary<string, string> english = Resources.Value[""];
        List<string> problems = [];

        foreach (Error error in ErrorsDeclared())
        {
            if (PassThroughCodes.Contains(error.Code))
            {
                continue;
            }

            string key = KeyPrefix + error.Code.Replace('.', '_');

            if (!english.TryGetValue($"{key}_{error.Arguments.Count}", out string? template) &&
                !english.TryGetValue(key, out template))
            {
                problems.Add($"{error.Code}: no {key} resource");
                continue;
            }

            int highest = PlaceholderIndexes(template).DefaultIfEmpty(-1).Max();
            if (highest >= error.Arguments.Count)
            {
                problems.Add($"{error.Code}: {key} uses {{{highest}}} but the error has {error.Arguments.Count} argument(s)");
            }
        }

        problems.Should().BeEmpty();
    }

    [Fact]
    public void EveryErrorResource_Should_BelongToAnErrorCode()
    {
        HashSet<string> codes = [.. ErrorsDeclared().Select(error => KeyPrefix + error.Code.Replace('.', '_'))];

        List<string> orphans = [.. Resources.Value[""].Keys
            .Where(key => key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            .Where(key => !codes.Contains(key) && !codes.Contains(ArityFree(key)))];

        orphans.Should().BeEmpty();
    }

    private static IEnumerable<Error> ErrorsDeclared()
    {
        IEnumerable<Type> errorClasses = new[] { DomainAssembly.Instance, ApplicationAssembly.Instance }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsAbstract && type.IsSealed && type.Name.EndsWith("Errors", StringComparison.Ordinal));

        foreach (Type type in errorClasses)
        {
            const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

            foreach (FieldInfo field in type.GetFields(Statics).Where(f => f.FieldType == typeof(Error)))
            {
                yield return (Error)field.GetValue(null)!;
            }

            foreach (MethodInfo method in type.GetMethods(Statics).Where(m => m.ReturnType == typeof(Error) && !m.IsSpecialName))
            {
                ParameterInfo[] parameters = method.GetParameters();

                yield return (Error)method.Invoke(null, [.. parameters.Select(SampleFor)])!;

                if (parameters.Any(p => p.ParameterType == typeof(string) && IsNullable(p)))
                {
                    yield return (Error)method.Invoke(null, [.. parameters.Select(p => IsNullable(p) ? null : SampleFor(p))])!;
                }
            }
        }
    }

    private static object? SampleFor(ParameterInfo parameter)
    {
        Type type = parameter.ParameterType;

        if (type == typeof(string))
        {
            return "sample";
        }

        if (type == typeof(Error))
        {
            return Error.None;
        }

        if (type.IsAssignableFrom(typeof(DriveFailure[])))
        {
            return Array.Empty<DriveFailure>();
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static bool IsNullable(ParameterInfo parameter) =>
        new NullabilityInfoContext().Create(parameter).WriteState == NullabilityState.Nullable;

    private static string ArityFree(string key) => ArityFreeRegex().Replace(key, string.Empty);

    private static string Placeholders(string value) =>
        string.Join(",", PlaceholderIndexes(value).Distinct().Order());

    private static IEnumerable<int> PlaceholderIndexes(string value) =>
        PlaceholderRegex().Matches(value).Select(match => int.Parse(match.Groups[1].Value));

    private static Dictionary<string, Dictionary<string, string>> LoadResources()
    {
        string languages = Path.Combine(RepositoryRoot(), "src", "Helix.App", "Resources", "Languages");

        return Cultures.ToDictionary(
            culture => culture,
            culture => XDocument
                .Load(Path.Combine(languages, culture.Length == 0 ? "AppResources.resx" : $"AppResources.{culture}.resx"))
                .Root!
                .Elements("data")
                .ToDictionary(
                    data => (string)data.Attribute("name")!,
                    data => (string?)data.Element("value") ?? string.Empty));
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Helix.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Helix.slnx was not found above the test output.");
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"_\d+$")]
    private static partial Regex ArityFreeRegex();
}
