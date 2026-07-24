using CommunityToolkit.Maui.Core.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Helix.App.Modals.Auditlogs.Search;
using Helix.App.Models;
using Helix.Application.Auditlogs;
using Helix.Domain.Auditlogs;
using SharedKernel;
using System.Collections.ObjectModel;

namespace Helix.App.Pages.Auditlogs;

internal sealed partial class AuditlogsViewModel : BaseViewModel
{
    public AuditlogsViewModel()
    {
        RegisterMessages();
    }

    [ObservableProperty]
    private ObservableCollection<AuditlogDisplay> _auditlogs = [];

    [RelayCommand]
    private static void OpenSearchAuditlogsModal()
    {
        WeakReferenceMessenger.Default.Send(new SearchAuditlogsMessage(true));
    }

    [RelayCommand]
    private async Task GetAuditlogsAsync()
    {
        Result<List<Auditlog>> result = await ScopedHandler.HandleAsync((GetAuditlogs h) => h.Handle());
        if (result.IsSuccess)
        {
            List<Auditlog> auditlogs = result.Value;

            Auditlogs = auditlogs.Select(a => new AuditlogDisplay(a)).ToObservableCollection();
        }
    }

    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<AuditlogsSearchedMessage>(this, (r, m) =>
        {
            IEnumerable<AuditlogDisplay> auditlogs = m.Auditlogs.Select(a => new AuditlogDisplay(a));

            Auditlogs = new(auditlogs);
        });
    }
}
