using Microsoft.Extensions.Logging;

namespace Helix.App.Behaviors;

/// <summary>
/// Makes a list row reachable and operable from the keyboard.
///
/// The row is one tab stop rather than eight: its status pill and icon chips are styled
/// <see cref="Border"/>s with tap gestures, and MAUI's <see cref="Button"/> takes text
/// rather than arbitrary content, so making each of them focusable would mean rebuilding
/// every interactive part of the row — and would put a hundred tab stops in front of a
/// user with thirteen drives. Focus lands on the row and the keys act on it, the way a
/// file manager's list does.
///
/// Windows only, like <see cref="Hover"/>. Mac Catalyst has no equivalent seam in MAUI
/// and the rows stay mouse-only there.
/// </summary>
internal static class RowFocus
{
    public static readonly BindableProperty OwnerProperty =
        BindableProperty.CreateAttached(
            "Owner",
            typeof(object),
            typeof(RowFocus),
            null,
            propertyChanged: OnOwnerChanged);

    public static object? GetOwner(BindableObject view) => view.GetValue(OwnerProperty);

    public static void SetOwner(BindableObject view, object? value) => view.SetValue(OwnerProperty, value);

    private static void OnOwnerChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not VisualElement element)
        {
            return;
        }

        Attach(element);

        // The platform view does not exist yet the first time this runs from XAML, and a
        // recycled row is handed a new one, so the attachment is redone on every handler.
        element.HandlerChanged -= OnHandlerChanged;
        element.HandlerChanged += OnHandlerChanged;
    }

    private static void OnHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is VisualElement element)
        {
            Attach(element);
        }
    }

#if WINDOWS
    private const string FocusedState = "Focused";

    private const string NormalState = "Normal";

    private static void Attach(VisualElement element)
    {
        if (element.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement platform)
        {
            return;
        }

        try
        {
            platform.IsTabStop = true;

            Rows.Remove(platform);
            Rows.Add(platform, element);

            platform.KeyDown -= OnKeyDown;
            platform.KeyDown += OnKeyDown;

            platform.GotFocus -= OnGotFocus;
            platform.GotFocus += OnGotFocus;

            platform.LostFocus -= OnLostFocus;
            platform.LostFocus += OnLostFocus;
        }
        catch (Exception ex)
        {
            AppLog.For(typeof(RowFocus)).LogDebug(ex, "Could not make a row focusable.");
        }
    }

    private static void OnGotFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SetState(sender, FocusedState);

    private static void OnLostFocus(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        SetState(sender, NormalState);

    private static void SetState(object sender, string state)
    {
        if (Owning(sender) is VisualElement element)
        {
            VisualStateManager.GoToState(element, state);
        }
    }

    private static void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        // Up and Down are answered here rather than by the row: moving between rows is
        // the list's business, not the drive's, and WinUI already knows what is above and
        // below a focused element without this having to find the CollectionView.
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Up:
                e.Handled = Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(
                    Microsoft.UI.Xaml.Input.FocusNavigationDirection.Up);
                return;

            case Windows.System.VirtualKey.Down:
                e.Handled = Microsoft.UI.Xaml.Input.FocusManager.TryMoveFocus(
                    Microsoft.UI.Xaml.Input.FocusNavigationDirection.Down);
                return;
        }

        if (Owning(sender) is not VisualElement element || GetOwner(element) is not IRowKeys row)
        {
            return;
        }

        bool control = IsDown(Windows.System.VirtualKey.Control);

        RowKey? key = e.Key switch
        {
            Windows.System.VirtualKey.Enter when !control => RowKey.Activate,
            Windows.System.VirtualKey.Space when !control => RowKey.Select,
            Windows.System.VirtualKey.Delete when !control => RowKey.Delete,
            Windows.System.VirtualKey.F2 when !control => RowKey.Edit,
            Windows.System.VirtualKey.D when control => RowKey.Diagnose,
            Windows.System.VirtualKey.O when control => RowKey.Open,
            _ => null,
        };

        if (key is null)
        {
            return;
        }

        e.Handled = row.OnRowKey(key.Value);
    }

    private static bool IsDown(Windows.System.VirtualKey key)
    {
        try
        {
            return Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(key)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        }
        catch (Exception ex)
        {
            AppLog.For(typeof(RowFocus)).LogDebug(ex, "Could not read the keyboard modifiers.");

            return false;
        }
    }

    // The platform view is what raises the events and the MAUI element is what carries the
    // owner and the visual state, so the pair is remembered here rather than walked back
    // through the handler, which a recycled row may already have replaced.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        Microsoft.UI.Xaml.UIElement,
        VisualElement> Rows = [];

    private static VisualElement? Owning(object sender) =>
        sender is Microsoft.UI.Xaml.UIElement platform && Rows.TryGetValue(platform, out VisualElement? element)
            ? element
            : null;
#else
    private static void Attach(VisualElement element)
    {
    }
#endif
}
