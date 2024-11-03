using System.Reflection;

namespace Helix.Infrastructure;

public static class InfrastructureAssembly
{
    public static readonly Assembly Instance = typeof(InfrastructureAssembly).Assembly;
}
