using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Resources.Languages;
using Helix.Application.Core.Security;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class PassphraseViewModel : BaseViewModel
{
    public const int MaxLength = 256;

    private TaskCompletionSource<string?>? _answer;

    public PassphraseViewModel()
    {
        Passphrase = string.Empty;
        ConfirmedPassphrase = string.Empty;
        Error = string.Empty;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(Message), nameof(AcceptText))]
    public partial bool Confirm { get; set; }

    [ObservableProperty]
    public partial string Passphrase { get; set; }

    [ObservableProperty]
    public partial string ConfirmedPassphrase { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    public bool HasError => !string.IsNullOrEmpty(Error);

    public string Title => Confirm ? AppResources.EncryptExport : AppResources.DecryptImport;

    public string Message => Confirm ? AppResources.EncryptExportMessage : AppResources.DecryptImportMessage;

    public string AcceptText => Confirm ? AppResources.Encrypt : AppResources.Decrypt;

    public int PassphraseMaxLength => MaxLength;

    public Task<string?> Ask(bool confirm)
    {
        Complete(null);
        Clear();

        Confirm = confirm;
        OnPropertyChanged(string.Empty);

        _answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        return _answer.Task;
    }

    [RelayCommand]
    private void Submit()
    {
        if (string.IsNullOrWhiteSpace(Passphrase))
        {
            return;
        }

        if (Confirm)
        {
            if (!PasswordPolicy.IsLongEnough(Passphrase))
            {
                Error = AppResources.PassphraseTooShort;
                return;
            }

            if (!string.Equals(Passphrase, ConfirmedPassphrase, StringComparison.Ordinal))
            {
                Error = AppResources.PassphrasesDontMatchMessage;
                ConfirmedPassphrase = string.Empty;
                return;
            }
        }

        string passphrase = Passphrase;

        Clear();
        Complete(passphrase);
    }

    [RelayCommand]
    private void Close() => Cancel();

    public void Cancel()
    {
        Clear();
        Complete(null);
    }

    private void Complete(string? passphrase)
    {
        TaskCompletionSource<string?>? answer = _answer;
        _answer = null;

        answer?.TrySetResult(passphrase);
    }

    private void Clear()
    {
        Passphrase = string.Empty;
        ConfirmedPassphrase = string.Empty;
        Error = string.Empty;
    }
}
