using FluentAssertions;
using Helix.Application;
using Helix.Domain;
using Helix.Infrastructure;
using NetArchTest.Rules;

namespace ArchitectureTests.Layers;

public sealed class LayerTests
{
    [Fact]
    public void DomainLayer_Should_NotHaveDependencyOn_ApplicationLayer()
    {
        TestResult result = Types.InAssembly(DomainAssembly.Instance)
            .Should()
            .NotHaveDependencyOn(ApplicationAssembly.Instance.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void DomainLayer_Should_NotHaveDependencyOn_InfrastructureLayer()
    {
        TestResult result = Types.InAssembly(DomainAssembly.Instance)
            .Should()
            .NotHaveDependencyOn(InfrastructureAssembly.Instance.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void ApplicationLayer_Should_NotHaveDependencyOn_InfrastructureLayer()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly.Instance)
            .Should()
            .NotHaveDependencyOn(InfrastructureAssembly.Instance.GetName().Name)
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
