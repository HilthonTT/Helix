using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Auditlogs.Search;

namespace Helix.App.Pages.Auditlogs;

public sealed partial class AuditlogsPage : ContentPage
{
	private static bool _searchAuditlogsModalOpen = false;

    private readonly AuditlogsViewModel _viewModel;

	public AuditlogsPage()
	{
		InitializeComponent();

        _viewModel = new AuditlogsViewModel();

        BindingContext = _viewModel;

        RegisterMessages();
    }

    protected override async void OnAppearing()
    {
        try
        {
            if (_viewModel.GetAuditlogsCommand.CanExecute(null))
            {
                await _viewModel.GetAuditlogsCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Something went wrong!", ex.Message, "Ok");
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

    private readonly Dictionary<AbsoluteLayout, int> _modalCloseTokens = [];

    private void OpenModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        // Invalidate any pending close: its delayed hide would otherwise blank out a
        // modal that was reopened within the 800 ms close animation.
        _modalCloseTokens[absoluteLayout] = _modalCloseTokens.GetValueOrDefault(absoluteLayout) + 1;

        absoluteLayout.IsVisible = true;
        contentView.Opacity = 0;
        _ = contentView.FadeToAsync(1, 800, Easing.CubicIn);
        _ = BlockScreen.FadeToAsync(0.8, 800, Easing.CubicOut);

        BlockScreen.InputTransparent = false;
    }

    private async Task CloseModalInternal(AbsoluteLayout absoluteLayout, ContentView contentView)
    {
        int token = _modalCloseTokens.GetValueOrDefault(absoluteLayout) + 1;
        _modalCloseTokens[absoluteLayout] = token;

        _ = contentView.FadeToAsync(0, 800, Easing.CubicOut);
        _ = BlockScreen.FadeToAsync(0, 800, Easing.CubicOut);
        BlockScreen.InputTransparent = true;

        await Task.Delay(800);

        if (_modalCloseTokens.GetValueOrDefault(absoluteLayout) == token)
        {
            absoluteLayout.IsVisible = false;
        }
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
