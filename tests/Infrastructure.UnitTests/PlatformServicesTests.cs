using FluentAssertions;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Startup;
using Helix.Application.Abstractions.Storage;
using Helix.Infrastructure;
using Helix.Infrastructure.Connector;
using Helix.Infrastructure.Desktop;
using Helix.Infrastructure.Startup;
using Helix.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.UnitTests;

public sealed class PlatformServicesTests
{
    private static ServiceProvider BuildProvider()
    {
        return new ServiceCollection()
            .AddLogging()
            .AddInfrastructure()
            .BuildServiceProvider();
    }

    [Fact]
    public void AddInfrastructure_Should_BindTheWindowsNasConnector()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetRequiredService<INasConnector>().Should().BeOfType<WindowsNasConnector>();
    }

    [Fact]
    public void AddInfrastructure_Should_BindTheWindowsShortcutServices()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IStartupService>().Should().BeOfType<WindowsStartupService>();
        scope.ServiceProvider.GetRequiredService<IDesktopService>().Should().BeOfType<WindowsDesktopService>();
    }

    [Fact]
    public void AddInfrastructure_Should_BindTheWindowsTrayIcon()
    {
        using ServiceProvider provider = BuildProvider();

        ITrayIcon trayIcon = provider.GetRequiredService<ITrayIcon>();

        trayIcon.Should().BeOfType<WindowsTrayIcon>();
        trayIcon.IsSupported.Should().BeTrue();
    }

    [Fact]
    public void AddInfrastructure_Should_KeepTheTrayIconASingleton()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetRequiredService<ITrayIcon>()
            .Should().BeSameAs(provider.GetRequiredService<ITrayIcon>());
    }

    [Fact]
    public void AddInfrastructure_Should_BindTheWindowsStorageProbe()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetRequiredService<IStorageProbe>().Should().BeOfType<WindowsStorageProbe>();
    }

    [Fact]
    public void AddInfrastructure_Should_KeepTheNasConnectorASingleton()
    {
        using ServiceProvider provider = BuildProvider();

        provider.GetRequiredService<INasConnector>()
            .Should().BeSameAs(provider.GetRequiredService<INasConnector>());
    }
}
