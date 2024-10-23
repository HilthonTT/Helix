using CommunityToolkit.Mvvm.ComponentModel;
using SharedKernel;

namespace Helix.App.Pages;

public abstract partial class BaseViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    public static Task DisplayErrorAsync(Error error)
    {
        return Shell.Current.DisplayAlert("Something went wrong!", error.Description, "Ok");
    }

    public static Task DisplaySuccessAsync(string message)
    {
        return Shell.Current.DisplayAlert("Success!", message, "Ok");
    }
}
