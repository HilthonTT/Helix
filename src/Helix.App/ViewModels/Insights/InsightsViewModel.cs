using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Storage.Contracts;
using Helix.Application.Features.Storage.Queries;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Globalization;

namespace Helix.App.ViewModels.Insights;

internal sealed partial class InsightsViewModel : BaseViewModel
{
    private const int DefaultRangeDays = 90;

    public InsightsViewModel()
    {
        Volumes = [];
        Reliability = [];
        RangeDays = DefaultRangeDays;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    [NotifyPropertyChangedFor(nameof(Is30Days))]
    [NotifyPropertyChangedFor(nameof(Is90Days))]
    [NotifyPropertyChangedFor(nameof(Is365Days))]
    public partial int RangeDays { get; set; }

    public string RangeText => string.Format(CultureInfo.CurrentCulture, AppResources.ReliabilityTitle, RangeDays);

    public bool Is30Days => RangeDays == 30;

    public bool Is90Days => RangeDays == 90;

    public bool Is365Days => RangeDays == 365;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolumes))]
    [NotifyPropertyChangedFor(nameof(ShowNoHistory))]
    public partial ObservableCollection<VolumeHistoryDisplay> Volumes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolumes))]
    [NotifyPropertyChangedFor(nameof(ShowNoHistory))]
    public partial bool HistoryLoaded { get; set; }

    public bool HasVolumes => Volumes.Count > 0;

    public bool ShowNoHistory => HistoryLoaded && !HasVolumes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReliability))]
    [NotifyPropertyChangedFor(nameof(ShowNoDrops))]
    [NotifyPropertyChangedFor(nameof(ReliabilitySummary))]
    public partial ObservableCollection<ReliabilityRowDisplay> Reliability { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoDrops))]
    public partial bool ReliabilityLoaded { get; set; }

    public bool HasReliability => Reliability.Count > 0;

    public bool ShowNoDrops => ReliabilityLoaded && !HasReliability;

    public string ReliabilitySummary => string.Format(
        CultureInfo.CurrentCulture,
        AppResources.ReliabilityEstateTitle,
        GetEstateReliability.WindowDays);

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;

            await Task.WhenAll(LoadHistoryAsync(), LoadReliabilityAsync());
        }
        catch (Exception ex)
        {
            AppLog.For<InsightsViewModel>().LogError(ex, "Could not load the insights page.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SetRangeAsync(string days)
    {
        if (!int.TryParse(days, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
            value == RangeDays ||
            IsBusy)
        {
            return;
        }

        RangeDays = value;

        try
        {
            IsBusy = true;

            await LoadHistoryAsync();
        }
        catch (Exception ex)
        {
            AppLog.For<InsightsViewModel>().LogError(ex, "Could not load the storage history.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        var request = new GetStorageHistory.Request(RangeDays);

        Result<List<VolumeHistory>> result = await Task.Run(
            () => ScopedHandler.HandleAsync((GetStorageHistory h) => h.Handle(request)));

        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        Volumes = [.. result.Value.Select(volume => new VolumeHistoryDisplay(volume))];
        HistoryLoaded = true;
    }

    private async Task LoadReliabilityAsync()
    {
        Result<List<DriveReliabilityEntry>> result = await Task.Run(
            () => ScopedHandler.HandleAsync((GetEstateReliability h) => h.Handle()));

        if (result.IsFailure)
        {
            await DisplayErrorAsync(result.Error);
            return;
        }

        Reliability = [.. result.Value
            .Where(entry => entry.Reliability.HasDropped)
            .Select(entry => new ReliabilityRowDisplay(entry))];
        ReliabilityLoaded = true;
    }
}
