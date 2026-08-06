using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Auditlogs;

namespace Helix.App.Models;

internal sealed partial class AuditlogDisplay : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial Guid UserId { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; }

    [ObservableProperty]
    public partial DateTime CreatedOnUtc { get; set; }

    [ObservableProperty]
    public partial DateTime? ModifiedOnUtc { get; set; }

    public AuditlogDisplay(Auditlog auditlog)
    {
        Id = auditlog.Id;
        UserId = auditlog.UserId;
        Message = auditlog.Message;
        CreatedOnUtc = auditlog.CreatedOnUtc;
        ModifiedOnUtc = auditlog.ModifiedOnUtc;
    }
}
