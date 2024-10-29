using CommunityToolkit.Maui.Core.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Models;
using Helix.Application.Auditlogs;
using Helix.Domain.Auditlogs;
using SharedKernel;
using System.Collections.ObjectModel;

namespace Helix.App.Pages.Auditlogs;

internal sealed partial class AuditlogsViewModel : BaseViewModel
{
    private readonly GetAuditlogs _getAuditlogs;

    public AuditlogsViewModel()
    {
        _getAuditlogs = App.ServiceProvider.GetRequiredService<GetAuditlogs>();

        GetAuditlogs();
    }

    [ObservableProperty]
    private ObservableCollection<AuditlogDisplay> _auditlogs = [];

    private void GetAuditlogs()
    {
        Task.Run(async () =>
        {
            Result<List<Auditlog>> result = await _getAuditlogs.Handle();
            if (result.IsSuccess)
            {
                List<Auditlog> auditlogs = result.Value;

                Auditlogs = auditlogs.Select(a => new AuditlogDisplay(a)).ToObservableCollection();
            }
        });
    }
}
