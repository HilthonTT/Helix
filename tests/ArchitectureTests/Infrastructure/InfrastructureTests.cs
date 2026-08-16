using FluentAssertions;
using Helix.Infrastructure;
using NetArchTest.Rules;

namespace ArchitectureTests.Infrastructure;

public sealed class InfrastructureTests
{
    /// <summary>
    /// Everything Helix declares in this assembly sits under <c>Helix.Infrastructure</c>,
    /// mirroring its folder. The project once carried leftover <c>Krello.Persistence.*</c>
    /// and <c>Helix.Persistence.*</c> namespaces that no folder matched; this keeps them
    /// from creeping back. Compiler- and toolchain-generated types are ignored.
    /// </summary>
    [Fact]
    public void Types_Should_ResideIn_InfrastructureNamespace()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly.Instance)
            .That()
            .ResideInNamespaceStartingWith("Helix")
            .Or()
            .ResideInNamespaceStartingWith("Krello")
            .Should()
            .ResideInNamespaceStartingWith("Helix.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Repositories_Should_BeSealed()
    {
        TestResult result = Types.InAssembly(InfrastructureAssembly.Instance)
            .That()
            .ResideInNamespace("Helix.Infrastructure.Database.Repositories")
            .Should()
            .BeSealed()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
