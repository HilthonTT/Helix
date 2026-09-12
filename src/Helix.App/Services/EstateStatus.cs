using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Messaging.Drives;
using Helix.App.Messaging.Storage;
using Helix.Application.Abstractions.Connector;
using Helix.Application.Abstractions.Storage;
using Helix.Application.Features.Drives.Queries;
using Helix.Application.Features.Storage.Contracts;
using Helix.Domain.Drives;
using Microsoft.Extensions.Logging;

namespace Helix.App.Services;

public sealed partial class EstateStatus : ObservableObject
{
    private readonly INasConnector _nasConnector;
    private readonly IStorageProbe _storageProbe;
    private readonly ILogger<EstateStatus> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private bool _isRunning;
    private int _pending;

    public EstateStatus(
        INasConnector nasConnector,
        IStorageProbe storageProbe,
        ILogger<EstateStatus> logger)
    {
        _nasConnector = nasConnector;
        _storageProbe = storageProbe;
        _logger = logger;

        RegisterMessages();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectedSummary))]
    [NotifyPropertyChangedFor(nameof(HasDrives))]
    public partial int DriveCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectedSummary))]
    public partial int ConnectedCount { get; set; }

    [ObservableProperty]
    public partial string StorageSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasLowSpace { get; set; }

    public string ConnectedSummary => $"{ConnectedCount} / {DriveCount}";

    public bool HasDrives => DriveCount > 0;

    public void Start()
    {
        _isRunning = true;

        _ = RefreshAsync();
    }

    public void Stop()
    {
        _isRunning = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            DriveCount = 0;
            ConnectedCount = 0;
            StorageSummary = string.Empty;
            HasLowSpace = false;
        });
    }

    public async Task RefreshAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        if (!await _gate.WaitAsync(0))
        {
            Interlocked.Exchange(ref _pending, 1);

            return;
        }

        try
        {
            do
            {
                Interlocked.Exchange(ref _pending, 0);

                await RefreshCoreAsync();
            }
            while (_isRunning && Interlocked.Exchange(ref _pending, 0) == 1);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            Result<List<Drive>> result = await ScopedHandler.HandleAsync((GetDrives h) => h.Handle());
            if (result.IsFailure)
            {
                _logger.LogDebug("The sidebar could not read the drives: {Reason}", result.Error.Description);

                return;
            }

            List<Drive> drives = result.Value;

            HashSet<string> connectedLetters = _nasConnector.GetConnectedLetters();

            string[] connected = [.. drives
                .Select(drive => drive.Letter)
                .Where(connectedLetters.Contains)];

            IReadOnlyList<VolumeUsage> volumes = await _storageProbe.ProbeAsync(connected);

            string storage = StorageUsageHelper.FormatCombined(
                volumes.Sum(volume => volume.UsedBytes),
                volumes.Sum(volume => volume.TotalBytes));

            if (!_isRunning)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                DriveCount = drives.Count;
                ConnectedCount = connected.Length;
                StorageSummary = storage;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The sidebar status could not be refreshed.");
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<EstateStatus, CheckDrivesStatusMessage>(
            this, (r, m) => _ = r.RefreshAsync());

        WeakReferenceMessenger.Default.Register<EstateStatus, DriveCreatedMessage>(
            this, (r, m) => _ = r.RefreshAsync());

        WeakReferenceMessenger.Default.Register<EstateStatus, DriveDeletedMessage>(
            this, (r, m) => _ = r.RefreshAsync());

        WeakReferenceMessenger.Default.Register<EstateStatus, StorageAlertsChangedMessage>(
            this, (r, m) => MainThread.BeginInvokeOnMainThread(() => r.HasLowSpace = m.VolumeCount > 0));
    }
}
