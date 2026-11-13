using Helix.App.Resources.Languages;
using Helix.Application.Features.Drives.Contracts;

namespace Helix.App.Models;

internal sealed class ServerOption
{
    public string Address { get; }

    public string Title { get; }

    public string Detail { get; }

    public ServerOption(DiscoveredServer server)
    {
        Address = server.Address;
        Title = server.DisplayName;

        List<string> parts = [];

        if (!string.Equals(server.DisplayName, server.Address, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(server.Address);
        }

        if (server.Model is not null)
        {
            parts.Add(server.Model);
        }

        if (server.AlreadyAdded)
        {
            parts.Add(string.Format(
                AppResources.ServerAlreadyAdded,
                string.Join(", ", server.ExistingLetters.Select(letter => $"{letter}:"))));
        }

        Detail = string.Join(" · ", parts);
    }
}
