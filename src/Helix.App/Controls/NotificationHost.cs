using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Icons;
using Helix.App.Messaging.Notifications;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Microsoft.Maui.Controls.Shapes;

// Unaliased, `Application` binds to the Helix.Application namespace from here.
using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Controls;

/// <summary>
/// The stack of dismissible banners that replaced the app's success and failure alerts.
/// One sits in the root grid of every page, above the modal layer.
/// </summary>
/// <remarks>
/// Built in code rather than XAML because the banners are created as they arrive and
/// there is no list to template — and because a page then hosts it with a single
/// self-configuring tag rather than a block of layout it would have to keep in step with
/// the other five pages.
///
/// Only the host on the page the user is looking at accepts a message. Shell keeps the
/// pages it has visited alive, so every host is subscribed at once; without the check, a
/// banner would be waiting on the audit log and the settings page for a drive the user
/// connected from the dashboard.
/// </remarks>
internal sealed class NotificationHost : ContentView
{
    /// <summary>How long a banner stays up before it dismisses itself.</summary>
    /// <remarks>
    /// A failure gets noticeably longer than a success: the success only confirms what
    /// the user just watched happen, while the failure is text they have to read, and
    /// possibly act on. Nothing is lost when one goes — the audit log and the diagnostics
    /// file are what the record is for.
    /// </remarks>
    private static readonly Dictionary<NotificationKind, TimeSpan> Lifetimes = new()
    {
        [NotificationKind.Success] = TimeSpan.FromSeconds(5),
        [NotificationKind.Info] = TimeSpan.FromSeconds(6),
        [NotificationKind.Warning] = TimeSpan.FromSeconds(9),
        [NotificationKind.Error] = TimeSpan.FromSeconds(14)
    };

    /// <summary>
    /// Banners kept on screen at once. Past this the oldest goes, so a sweep that fails
    /// over thirteen shares cannot bury the page it is reporting on.
    /// </summary>
    private const int MaximumBanners = 4;

    private const uint EnterMs = 180;
    private const uint LeaveMs = 130;

    private readonly VerticalStackLayout _stack;

    private Page? _owner;

    public NotificationHost()
    {
        _stack = new VerticalStackLayout { Spacing = 10 };

        Content = _stack;

        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        Margin = new Thickness(28, 24);
        MaximumWidthRequest = 420;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (_, message) =>
        {
            if (message.Handled || !IsOnScreen())
            {
                return;
            }

            message.Handled = true;

            Show(message);
        });

        // The page is cached by Shell and raises this every time it is navigated back to,
        // which is exactly when a message held while the user was elsewhere should land.
        _owner = FindOwningPage();

        if (_owner is not null)
        {
            _owner.Appearing += OnOwnerAppearing;
        }

        DrainHeld();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        WeakReferenceMessenger.Default.Unregister<NotificationMessage>(this);

        if (_owner is not null)
        {
            _owner.Appearing -= OnOwnerAppearing;
            _owner = null;
        }
    }

    private void OnOwnerAppearing(object? sender, EventArgs e) => DrainHeld();

    private void DrainHeld()
    {
        if (!IsOnScreen())
        {
            return;
        }

        foreach (NotificationMessage message in Notifier.DrainHeld())
        {
            message.Handled = true;

            Show(message);
        }
    }

    /// <summary>
    /// Whether this host is the one the user can actually see.
    /// </summary>
    /// <remarks>
    /// A null <c>CurrentPage</c> is treated as "yes": that is the state during the first
    /// navigation, and a banner shown a moment early beats a failure that is never
    /// reported.
    /// </remarks>
    private bool IsOnScreen()
    {
        // Only ever asked between Loaded and Unloaded, so the host is attached by
        // construction; what is left to establish is whether its page is the one in front.
        Page? owner = _owner ?? FindOwningPage();
        if (owner is null)
        {
            return false;
        }

        Page? current = Shell.Current?.CurrentPage;

        return current is null || current == owner;
    }

    private Page? FindOwningPage()
    {
        Element? element = Parent;

        while (element is not null)
        {
            if (element is Page page)
            {
                return page;
            }

            element = element.Parent;
        }

        return null;
    }

    private void Show(NotificationMessage message)
    {
        // The same text arriving twice is one thing happening twice — a sweep retrying,
        // or a group whose drives share a NAS. Restarting the banner's clock says so
        // without stacking identical copies.
        foreach (Banner existing in _stack.Children.OfType<Banner>())
        {
            if (existing.Kind == message.Kind &&
                string.Equals(existing.Message, message.Text, StringComparison.Ordinal))
            {
                existing.RestartLifetime();

                return;
            }
        }

        while (_stack.Children.Count >= MaximumBanners)
        {
            if (_stack.Children[0] is Banner oldest)
            {
                _ = DismissAsync(oldest);
            }

            // Dismissal animates, so the child is still there. Detach it now so the count
            // is right for the banner being added.
            _stack.Children.RemoveAt(0);
        }

        var banner = new Banner(message.Kind, message.Text, Dispatcher);

        banner.Dismissed += (_, _) => _ = DismissAsync(banner);

        _stack.Children.Add(banner);

        banner.Opacity = 0;
        banner.TranslationY = -10;

        _ = Task.WhenAll(
            banner.FadeToAsync(1, EnterMs, Easing.CubicOut),
            banner.TranslateToAsync(0, 0, EnterMs, Easing.CubicOut));
    }

    private async Task DismissAsync(Banner banner)
    {
        banner.StopLifetime();

        await Task.WhenAll(
            banner.FadeToAsync(0, LeaveMs, Easing.CubicIn),
            banner.TranslateToAsync(0, -8, LeaveMs, Easing.CubicIn));

        _stack.Children.Remove(banner);
    }

    /// <summary>One banner: glyph, message, and a close chip.</summary>
    private sealed class Banner : Border
    {
        private readonly IDispatcherTimer _lifetime;

        public Banner(NotificationKind kind, string message, IDispatcher dispatcher)
        {
            Kind = kind;
            Message = message;

            (string glyph, string lightKey, string darkKey) = Appearance(kind);

            StrokeThickness = 1;
            StrokeShape = new RoundRectangle { CornerRadius = 12 };
            Padding = new Thickness(14, 12);

            this.SetAppThemeColor(BackgroundColorProperty, Resource("SurfaceLight"), Resource("SurfaceDark"));
            this.SetAppTheme(
                StrokeProperty,
                new SolidColorBrush(Resource("BorderLight")),
                new SolidColorBrush(Resource("BorderDark")));

            var icon = new Label
            {
                Text = glyph,
                FontFamily = IconFontFamily,
                FontSize = 15,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 1, 0, 0)
            };

            icon.SetAppThemeColor(Label.TextColorProperty, Resource(lightKey), Resource(darkKey));

            var text = new Label
            {
                Text = message,
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Center
            };

            text.SetAppThemeColor(Label.TextColorProperty, Resource("TextLight"), Resource("TextDark"));

            var close = new Label
            {
                Text = IconFont.Times,
                FontFamily = IconFontFamily,
                FontSize = 12,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 0, 0)
            };

            close.SetAppThemeColor(Label.TextColorProperty, Resource("TextFaintLight"), Resource("TextFaintDark"));

            var closeTap = new TapGestureRecognizer();
            closeTap.Tapped += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);

            close.GestureRecognizers.Add(closeTap);

            ToolTipProperties.SetText(close, AppResources.Dismiss);

            var layout = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 11
            };

            layout.Add(icon);
            layout.Add(text, 1);
            layout.Add(close, 2);

            Content = layout;

            SemanticProperties.SetDescription(this, message);

            _lifetime = dispatcher.CreateTimer();
            _lifetime.Interval = Lifetimes[kind];
            _lifetime.IsRepeating = false;
            _lifetime.Tick += (_, _) => Dismissed?.Invoke(this, EventArgs.Empty);
            _lifetime.Start();
        }

        public event EventHandler? Dismissed;

        public NotificationKind Kind { get; }

        public string Message { get; }

        public void RestartLifetime()
        {
            _lifetime.Stop();
            _lifetime.Start();
        }

        public void StopLifetime() => _lifetime.Stop();

        private static (string Glyph, string LightKey, string DarkKey) Appearance(NotificationKind kind) => kind switch
        {
            NotificationKind.Success => (IconFont.CheckCircle, "SuccessLight", "SuccessDark"),
            NotificationKind.Warning => (IconFont.ExclamationTriangle, "WarningLight", "WarningDark"),
            NotificationKind.Error => (IconFont.ExclamationCircle, "DangerLight", "DangerDark"),
            _ => (IconFont.InfoCircle, "Brand", "Brand")
        };

        /// <summary>
        /// The glyph font under the key the styles use, so the banner cannot end up on a
        /// different one than every other icon in the app.
        /// </summary>
        private static string IconFontFamily =>
            AppBase.Current?.Resources.TryGetValue("FontIcon", out object? value) == true && value is string family
                ? family
                : "FontAwesome";

        /// <summary>
        /// Looks a palette entry up by key, so the banner keeps using the same colours as
        /// the rest of the app rather than a second set written out here.
        /// </summary>
        private static Color Resource(string key)
        {
            if (AppBase.Current?.Resources.TryGetValue(key, out object? value) == true && value is Color color)
            {
                return color;
            }

            return Colors.Grey;
        }
    }
}
