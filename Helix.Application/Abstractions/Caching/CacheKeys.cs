namespace Helix.Application.Abstractions.Caching;

internal static class CacheKeys
{
    public static class Drives
    {
        public const string MainPrefix = "Drives";

        public const string All = "Drives";

        public static string GetById(Guid id) => $"{MainPrefix}-by-id-{id}";
    }
}
