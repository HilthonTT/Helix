using System.Reflection;

namespace Helix.Domain;

public static class DomainAssembly
{
    public static readonly Assembly Instance = typeof(DomainAssembly).Assembly;
}
