using System.Reflection;

namespace Helix.Application;

public static class ApplicationAssembly
{
    public static readonly Assembly Instance = typeof(ApplicationAssembly).Assembly;
}
