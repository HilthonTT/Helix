using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Application.Abstractions.Connector;

namespace Helix.App.Models;

internal sealed partial class MappingOption : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public MappedShare Mapping { get; }

    public string Label { get; }

    public MappingOption(MappedShare mapping)
    {
        Mapping = mapping;
        Label = $@"{mapping.Letter}: — \{mapping.Host}\{mapping.Share}";
        IsSelected = true;
    }
}
