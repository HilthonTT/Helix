using Helix.App.Resources.Languages;
using System.ComponentModel;
using System.Globalization;

namespace Helix.App;

internal sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private LocalizationResourceManager()
    {
        AppResources.Culture = CultureInfo.CurrentCulture;
        _currentDate = FormatCurrentDate();
    }

    public static LocalizationResourceManager Instance { get; } = new();

    public object this[string resourceKey] => 
        AppResources.ResourceManager.GetObject(resourceKey, AppResources.Culture) ?? Array.Empty<byte>();

    public void SetCulture(CultureInfo culture)
    {
        AppResources.Culture = culture;
        UpdateCurrentDate();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private string _currentDate;
    public string CurrentDate
    {
        get => _currentDate;
        private set
        {
            if (_currentDate != value)
            {
                _currentDate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentDate)));
            }
        }
    }

    private void UpdateCurrentDate()
    {
        CurrentDate = FormatCurrentDate();
    }

    private string FormatCurrentDate()
    {
        return DateTime.Now.ToString("dddd, MMMM dd yyyy", AppResources.Culture);
    }
}
