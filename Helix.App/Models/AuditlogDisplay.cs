using CommunityToolkit.Mvvm.ComponentModel;
using Helix.Domain.Auditlogs;

namespace Helix.App.Models;

internal sealed partial class AuditlogDisplay : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private Guid _userId;

    [ObservableProperty]
    private string _message;

    [ObservableProperty]
    private DateTime _createdOnUtc;

    [ObservableProperty]
    private DateTime? _modifiedOnUtc;

    public AuditlogDisplay(Auditlog auditlog)
    {
        Id = auditlog.Id;
        UserId = auditlog.UserId;
        Message = auditlog.Message;
        CreatedOnUtc = auditlog.CreatedOnUtc;
        ModifiedOnUtc = auditlog.ModifiedOnUtc;
    }
}
