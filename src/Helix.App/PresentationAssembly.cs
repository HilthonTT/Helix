using System.Reflection;

namespace Helix.App;

public static class PresentationAssembly
{
    public static readonly Assembly Instance = typeof(PresentationAssembly).Assembly;
}
