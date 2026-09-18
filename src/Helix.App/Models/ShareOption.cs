using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
using Helix.Application.Features.Drives.Contracts;

namespace Helix.App.Models;

internal sealed partial class ShareOption : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public string Name { get; }

    public bool CanSelect { get; }

    public string Label { get; }

    public ShareOption(AvailableShare share)
    {
        Name = share.Name;
        CanSelect = !share.AlreadyAdded;
        Label = share.AlreadyAdded
            ? $"{share.Name} — {string.Format(AppResources.ShareAlreadyAdded, share.ExistingLetter)}"
            : share.Name;
    }
}
