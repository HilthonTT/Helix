using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Features.Drives.Contracts;
using Helix.Domain.Drives;
using Helix.Domain.Users;
using System.Net;

namespace Helix.Application.Features.Drives.Queries;

public sealed class DiscoverServers(
    IDriveRepository driveRepository,
    ILoggedInUser loggedInUser,
    INasDiscovery nasDiscovery) : IHandler
{
    private const string LocalSuffix = ".local";

    public async Task<Result<List<DiscoveredServer>>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<List<DiscoveredServer>>(AuthenticationErrors.InvalidPermissions);
        }

        IReadOnlyList<DiscoveredHost> hosts = await nasDiscovery.DiscoverAsync(cancellationToken);

        List<Drive> drives = await driveRepository.GetAsNoTrackingAsync(loggedInUser.UserId, cancellationToken);

        return hosts
            .DistinctBy(host => host.Address, StringComparer.OrdinalIgnoreCase)
            .Select(host => new DiscoveredServer(
                host.Address,
                DisplayNameOf(host),
                string.IsNullOrWhiteSpace(host.Model) ? null : host.Model.Trim(),
                LettersOn(host, drives)))
            .OrderBy(server => server.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(server => server.Address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string DisplayNameOf(DiscoveredHost host)
    {
        if (!string.IsNullOrWhiteSpace(host.Name))
        {
            return host.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(host.HostName))
        {
            return WithoutLocalSuffix(host.HostName.Trim());
        }

        return host.Address;
    }

    private static List<string> LettersOn(DiscoveredHost host, List<Drive> drives)
    {
        HashSet<string> spellings = SpellingsOf(host);

        return drives
            .Where(drive => spellings.Contains(drive.Host.Trim()) || spellings.Contains(FirstLabelOf(drive.Host.Trim())))
            .Select(drive => drive.Letter)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HashSet<string> SpellingsOf(DiscoveredHost host)
    {
        HashSet<string> spellings = new(StringComparer.OrdinalIgnoreCase) { host.Address };

        foreach (string? name in new[] { host.Name, host.HostName })
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            string trimmed = name.Trim();

            spellings.Add(trimmed);
            spellings.Add(WithoutLocalSuffix(trimmed));
        }

        if (!string.IsNullOrWhiteSpace(host.HostName))
        {
            spellings.Add(FirstLabelOf(host.HostName.Trim()));
        }

        return spellings;
    }

    private static string WithoutLocalSuffix(string name) =>
        name.EndsWith(LocalSuffix, StringComparison.OrdinalIgnoreCase) && name.Length > LocalSuffix.Length
            ? name[..^LocalSuffix.Length]
            : name;

    private static string FirstLabelOf(string name)
    {
        if (IPAddress.TryParse(name, out _))
        {
            return name;
        }

        int dot = name.IndexOf('.');

        return dot > 0 ? name[..dot] : name;
    }
}
