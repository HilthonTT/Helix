using FluentAssertions;
using Helix.Infrastructure;
using NetArchTest.Rules;

namespace ArchitectureTests.Infrastructure;

public sealed class InfrastructureTests
{
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
