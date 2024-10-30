using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Auditlogs.Search;

namespace Helix.App.Pages.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{
	private static bool _searchAuditlogsModalOpen = false;

	public AuditlogsPage()
	{
		InitializeComponent();

		BindingContext = new AuditlogsViewModel();

		RegisterMessages();
    }

    protected override async void OnAppearing()
    {
		if (BindingContext is not AuditlogsViewModel viewModel)
		{
			return;
		}

		if (viewModel.GetAuditlogsCommand.CanExecute(null))
		{
			await viewModel.GetAuditlogsCommand.ExecuteAsync(null);
		}
    }

    private async Task OpenSearchAuditlogsAsync(bool show)
    {
        if (show)
        {
            OpenModalInternal(SearchAuditlogsLayout, SearchAuditlogsView);
            _searchAuditlogsModalOpen = true;
        }
        else
        {
            await CloseModalInternal(SearchAuditlogsLayout, SearchAuditlogsView);
            _searchAuditlogsModalOpen = false;
        }
    }

    private void OpenModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        absoluteLayout.IsVisible = true;
        contentView.Opacity = 0;
        _ = contentView.FadeTo(1, 800, Easing.CubicIn);
        _ = BlockScreen.FadeTo(0.8, 800, Easing.CubicOut);

        BlockScreen.InputTransparent = false;
    }

    private async Task CloseModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        _ = contentView.FadeTo(0, 800, Easing.CubicOut);
        _ = BlockScreen.FadeTo(0, 800, Easing.CubicOut);
        BlockScreen.InputTransparent = true;

        await Task.Delay(800);
        absoluteLayout.IsVisible = false;
    }

    private void RegisterMessages()
	{
		WeakReferenceMessenger.Default.Register<SearchAuditlogsMessage>(this, async (r, m) =>
		{
            bool isAlreadyOpen = _searchAuditlogsModalOpen && m.Value;
            if (isAlreadyOpen)
            {
                return;
            }

            await OpenSearchAuditlogsAsync(m.Value);
        });
	}
}