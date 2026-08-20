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

/// <summary>
/// Locks down the per-OS half of <c>AddInfrastructure</c>.
/// </summary>
/// <remarks>
/// The three platform services are chosen by <c>#if WINDOWS</c> / <c>#elif MACCATALYST</c>.
/// A preprocessor symbol that stopped being defined would not break the build — it would
/// silently fall through to the <c>#else</c> and throw on the first drive connection
/// instead. These assertions turn that into a test failure. This suite only runs on the
/// Windows head, so the macOS bindings are covered by the macOS CI job compiling at all.
/// </remarks>
public sealed class PlatformServicesTests
{
    private static ServiceProvider BuildProvider()
    {
        // Logging is normally brought in by the MAUI host, and several of these services
        // take an ILogger<T>. Added here so the container under test is the same shape as
        // the real one.
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

        // It owns a window and a message-loop thread; a second instance would put a
        // second icon in the tray.
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

        // Viewmodels cache this in a field across per-operation scopes.
        provider.GetRequiredService<INasConnector>()
            .Should().BeSameAs(provider.GetRequiredService<INasConnector>());
    }
}
