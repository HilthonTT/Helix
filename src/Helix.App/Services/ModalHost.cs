namespace Helix.App.Services;

internal sealed class ModalHost
{
    private const double ScrimOpacity = 0.55;
    private const uint OpenMs = 190;
    private const uint SettleMs = 260;
    private const uint CloseMs = 130;

    private readonly VisualElement _scrim;
    private readonly Dictionary<string, Sheet> _sheets = [];
    private readonly Dictionary<string, int> _closeTokens = [];

    private string? _current;

    public ModalHost(VisualElement scrim)
    {
        _scrim = scrim;
        _scrim.Opacity = 0;
        _scrim.InputTransparent = true;

        if (_scrim is View scrimView)
        {
            var dismiss = new TapGestureRecognizer();
            dismiss.Tapped += (_, _) => _ = HideCurrentAsync();

            scrimView.GestureRecognizers.Add(dismiss);
        }
    }

    public string? Current => _current;

    public bool IsOpen(string key) => _current == key;

    public void Register(string key, AbsoluteLayout layout, VisualElement sheet)
    {
        _sheets[key] = new Sheet(layout, sheet);

        layout.IsVisible = false;
    }

    public async Task ShowAsync(string key)
    {
        if (!_sheets.TryGetValue(key, out Sheet? target) || _current == key)
        {
            return;
        }

        if (_current is string open)
        {
            await HideAsync(open);
        }

        _closeTokens[key] = _closeTokens.GetValueOrDefault(key) + 1;
        _current = key;

        target.Layout.IsVisible = true;
        target.Content.Opacity = 0;
        target.Content.Scale = 0.96;
        target.Content.TranslationY = 14;

        _scrim.InputTransparent = false;

        _ = _scrim.FadeToAsync(ScrimOpacity, OpenMs, Easing.CubicOut);
        _ = target.Content.FadeToAsync(1, OpenMs, Easing.CubicOut);
        _ = target.Content.ScaleToAsync(1, SettleMs, Easing.CubicOut);

        await target.Content.TranslateToAsync(0, 0, SettleMs, Easing.CubicOut);
    }

    public async Task HideAsync(string key)
    {
        if (!_sheets.TryGetValue(key, out Sheet? target))
        {
            return;
        }

        int token = _closeTokens.GetValueOrDefault(key) + 1;
        _closeTokens[key] = token;

        if (_current == key)
        {
            _current = null;
        }

        _scrim.InputTransparent = true;

        _ = _scrim.FadeToAsync(0, CloseMs, Easing.CubicIn);
        _ = target.Content.ScaleToAsync(0.97, CloseMs, Easing.CubicIn);
        _ = target.Content.TranslateToAsync(0, 10, CloseMs, Easing.CubicIn);

        await target.Content.FadeToAsync(0, CloseMs, Easing.CubicIn);

        if (_closeTokens.GetValueOrDefault(key) == token)
        {
            target.Layout.IsVisible = false;
        }
    }

    public Task HideCurrentAsync()
    {
        return _current is string open ? HideAsync(open) : Task.CompletedTask;
    }

    public Task ToggleAsync(string key, bool show)
    {
        if (show)
        {
            return IsOpen(key) ? Task.CompletedTask : ShowAsync(key);
        }

        return HideAsync(key);
    }

    private sealed record Sheet(AbsoluteLayout Layout, VisualElement Content);

#if WINDOWS
    public void AttachEscapeToDismiss(Page page)
    {
        void Attach()
        {
            if (page.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement element)
            {
                return;
            }

            element.KeyDown -= OnKeyDown;
            element.KeyDown += OnKeyDown;
        }

        page.HandlerChanged += (_, _) => Attach();

        Attach();
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Escape || _current is null)
        {
            return;
        }

        e.Handled = true;

        _ = HideCurrentAsync();
    }
#else
    public void AttachEscapeToDismiss(Page page)
    {
    }
#endif
}
