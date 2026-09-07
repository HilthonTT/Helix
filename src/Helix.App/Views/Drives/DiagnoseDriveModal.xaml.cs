using Helix.App.ViewModels.Drives;

namespace Helix.App.Views.Drives;

public sealed partial class DiagnoseDriveModal : ContentView
{
    public DiagnoseDriveModal()
    {
        InitializeComponent();

        BindingContext = new DiagnoseDriveViewModel();
    }
}
