using Helix.App.Resources.Languages;
using Helix.App.Services;

namespace Helix.App.Views.Users;

public sealed partial class RecoveryKeyPanel : ContentView
{
    public static readonly BindableProperty RecoveryKeyProperty = BindableProperty.Create(
        nameof(RecoveryKey),
        typeof(string),
        typeof(RecoveryKeyPanel),
        string.Empty,
        propertyChanged: (bindable, _, value) => ((RecoveryKeyPanel)bindable).KeyLabel.Text = value as string);

    public RecoveryKeyPanel()
    {
        InitializeComponent();
    }

    public string RecoveryKey
    {
        get => (string)GetValue(RecoveryKeyProperty);
        set => SetValue(RecoveryKeyProperty, value);
    }

    private async void OnCopyTapped(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrEmpty(RecoveryKey))
        {
            return;
        }

        try
        {
            await Clipboard.Default.SetTextAsync(RecoveryKey);

            Notifier.Success(AppResources.RecoveryKeyCopied);
        }
        catch (Exception ex)
        {
            Notifier.Error(ex.Message);
        }
    }
}
