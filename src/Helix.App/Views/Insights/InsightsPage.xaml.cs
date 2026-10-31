using Helix.App.Services;
using Helix.App.ViewModels.Insights;

namespace Helix.App.Views.Insights;

public sealed partial class InsightsPage : ContentPage
{
    private readonly InsightsViewModel _viewModel;

    public InsightsPage()
    {
        InitializeComponent();

        _viewModel = new InsightsViewModel();

        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        try
        {
            if (_viewModel.LoadCommand.CanExecute(null))
            {
                await _viewModel.LoadCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            Notifier.Error(ex.Message);
        }
    }
}
