using FluentAssertions;
using Helix.Application;
using Helix.Application.Abstractions.Handlers;
using NetArchTest.Rules;

namespace ArchitectureTests.Application;

public class ApplicationTests
{
    [Fact]
    public void Handlers_Should_BePublic()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly.Instance)
            .That()
            .ImplementInterface(typeof(IHandler))
            .Should()
            .BePublic()
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Handlers_Should_BeSealed()
    {
        TestResult result = Types.InAssembly(ApplicationAssembly.Instance)
           .That()
           .ImplementInterface(typeof(IHandler))
           .Should()
           .BeSealed()
           .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
