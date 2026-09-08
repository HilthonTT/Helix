using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Icons;
using Helix.App.Messaging.Notifications;
using Helix.App.Resources.Languages;
using Helix.App.Services;
using Microsoft.Maui.Controls.Shapes;

using AppBase = Microsoft.Maui.Controls.Application;

namespace Helix.App.Controls;

internal sealed class NotificationHost : ContentView
{
    private static readonly Dictionary<NotificationKind, TimeSpan> Lifetimes = new()
    {
        [NotificationKind.Success] = TimeSpan.FromSeconds(5),
        [NotificationKind.Info] = TimeSpan.FromSeconds(6),
        [NotificationKind.Warning] = TimeSpan.FromSeconds(9),
        [NotificationKind.Error] = TimeSpan.FromSeconds(14)
    };

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

        WeakReferenceMessenger.Default.Register<RetractNotificationMessage>(this, (_, message) => Retract(message.Line));

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
        WeakReferenceMessenger.Default.Unregister<RetractNotificationMessage>(this);

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

    private bool IsOnScreen()
    {
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

    private void Retract(string line)
    {
        foreach (Banner banner in _stack.Children.OfType<Banner>().ToList())
        {
            if (!NotificationText.TryRemove(banner.Message, line, out string? remaining))
            {
                continue;
            }

            if (remaining is null)
            {
                _ = DismissAsync(banner);

                continue;
            }

            banner.Rewrite(remaining);
        }
    }

    private void Show(NotificationMessage message)
    {
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

    private sealed class Banner : Border
    {
        private readonly IDispatcherTimer _lifetime;
        private readonly Label _text;

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

            _text = new Label
            {
                Text = message,
                FontSize = 13,
                LineBreakMode = LineBreakMode.WordWrap,
                VerticalOptions = LayoutOptions.Center
            };

            _text.SetAppThemeColor(Label.TextColorProperty, Resource("TextLight"), Resource("TextDark"));

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
            layout.Add(_text, 1);
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

        public string Message { get; private set; }

        public void Rewrite(string message)
        {
            Message = message;

            _text.Text = message;

            SemanticProperties.SetDescription(this, message);
        }

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

        private static string IconFontFamily =>
            AppBase.Current?.Resources.TryGetValue("FontIcon", out object? value) == true && value is string family
                ? family
                : "FontAwesome";

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
