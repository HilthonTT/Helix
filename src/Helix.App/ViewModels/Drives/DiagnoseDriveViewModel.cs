using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Models;
using Helix.App.Resources.Languages;
using Helix.App.ViewModels;
using Helix.Application.Features.Drives.Commands;
using Helix.Application.Features.Drives.Contracts;
using Helix.Application.Features.Drives.Queries;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace Helix.App.ViewModels.Drives;

internal sealed partial class DiagnoseDriveViewModel : BaseViewModel
{
    public DiagnoseDriveViewModel()
    {
        RegisterMessages();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial DriveDisplay? Drive { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<DiagnosticStepDisplay> Steps { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    public partial bool HasRun { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowReliability))]
    public partial DriveReliabilityDisplay? Reliability { get; set; }

    public bool ShowReliability => Reliability is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(ShowProblem))]
    [NotifyPropertyChangedFor(nameof(ShowHealthy))]
    public partial bool HasFailure { get; set; }

    public string Subtitle => Drive is null
        ? string.Empty
        : string.Format(AppResources.DiagnoseSubtitle, Drive.Name, Drive.Host);

    public string Summary => HasFailure ? AppResources.DiagnoseProblem : AppResources.DiagnoseHealthy;

    public bool ShowSummary => HasRun;

    public bool ShowHealthy => !HasFailure;

    public bool ShowProblem => HasFailure;

    private int _run;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Drive is null)
        {
            return;
        }

        int run = ++_run;

        try
        {
            IsBusy = true;

            HasRun = false;
            Steps = [];
            Reliability = null;

            _ = LoadReliabilityAsync(Drive.Id, run);

            var request = new DiagnoseDrive.Request(Drive.Id);

            Result<DriveDiagnosis> result =
                await ScopedHandler.HandleAsync((DiagnoseDrive h) => h.Handle(request));

            if (run != _run)
            {
                return;
            }

            if (result.IsFailure)
            {
                await DisplayErrorAsync(result.Error);
                return;
            }

            Steps = [.. result.Value.Steps.Select(step => new DiagnosticStepDisplay(step))];
            HasFailure = result.Value.HasFailure;
            HasRun = true;
        }
        finally
        {
            if (run == _run)
            {
                IsBusy = false;
            }
        }
    }

    private async Task LoadReliabilityAsync(Guid driveId, int run)
    {
        try
        {
            var request = new GetDriveReliability.Request(driveId);

            Result<DriveReliability> result = await Task.Run(() =>
                ScopedHandler.HandleAsync((GetDriveReliability h) => h.Handle(request)));

            if (run == _run && result.IsSuccess)
            {
                Reliability = new DriveReliabilityDisplay(result.Value);
            }
        }
        catch (Exception ex)
        {
            AppLog.For<DiagnoseDriveViewModel>().LogWarning(ex, "Reading the drive's connection history failed.");
        }
    }

    [RelayCommand]
    private static void Close()
    {
        WeakReferenceMessenger.Default.Send(new DiagnoseDriveMessage(false, null));
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<DiagnoseDriveMessage>(this, async (r, m) =>
        {
            if (!m.Value || m.Drive is null)
            {
                return;
            }

            Drive = m.Drive;

            try
            {
                await RunAsync();
            }
            catch (Exception ex)
            {
                AppLog.For<DiagnoseDriveViewModel>().LogError(ex, "Diagnosing the drive failed.");

                Close();
            }
        });
    }
}
