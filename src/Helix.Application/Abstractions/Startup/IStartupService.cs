namespace Helix.Application.Abstractions.Startup;

public interface IStartupService
{
    void ToggleStartup(bool value);

    bool IsSetToStartup();
}
