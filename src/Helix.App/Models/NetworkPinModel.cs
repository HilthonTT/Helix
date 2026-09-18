using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
using Helix.Application.Abstractions.Connector;
using Microsoft.Extensions.Logging;

namespace Helix.App.Models;

internal sealed partial class NetworkPinModel : ObservableObject
{
    private readonly INetworkLocation _location;

    private NetworkLocation? _current;
    private NetworkLocation? _pinned;
    private bool _loading;

    public NetworkPinModel()
    {
        _location = App.ServiceProvider.GetRequiredService<INetworkLocation>();

        Label = AppResources.OnlyOnNetworkUnknown;
    }

    public bool IsSupported => _location.IsSupported;

    [ObservableProperty]
    public partial bool IsPinned { get; set; }

    [ObservableProperty]
    public partial bool CanChange { get; set; }

    [ObservableProperty]
    public partial string Label { get; set; }

    public string? NetworkId => IsPinned ? _pinned?.Id : null;

    public string? NetworkName => IsPinned ? _pinned?.Name : null;

    public async Task LoadAsync(string? homeNetworkId = null, string? homeNetworkName = null)
    {
        _loading = true;

        try
        {
            _pinned = homeNetworkId is null
                ? null
                : new NetworkLocation(homeNetworkId, homeNetworkName ?? homeNetworkId);

            IsPinned = _pinned is not null;
        }
        finally
        {
            _loading = false;
        }

        _current = null;
        Refresh();

        if (!IsSupported)
        {
            return;
        }

        try
        {
            _current = await _location.GetCurrentAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<NetworkPinModel>().LogDebug(ex, "Could not read the current network.");
        }

        Refresh();
    }

    partial void OnIsPinnedChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _pinned = value ? _current : null;

        Refresh();
    }

    private void Refresh()
    {
        NetworkLocation? shown = _pinned ?? _current;

        Label = shown is null
            ? AppResources.OnlyOnNetworkUnknown
            : string.Format(AppResources.OnlyOnNetwork, shown.Name);

        CanChange = IsPinned || _current is not null;
    }
}
