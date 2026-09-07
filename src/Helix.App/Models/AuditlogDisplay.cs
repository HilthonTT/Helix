using CommunityToolkit.Mvvm.ComponentModel;
using Helix.App.Resources.Languages;
using Helix.Domain.Auditlogs;

namespace Helix.App.Models;

internal sealed partial class AuditlogDisplay : ObservableObject
{
    [ObservableProperty]
    public partial Guid Id { get; set; }

    [ObservableProperty]
    public partial Guid UserId { get; set; }

    [ObservableProperty]
    public partial AuditAction Action { get; set; }

    [ObservableProperty]
    public partial string? EntityName { get; set; }

    [ObservableProperty]
    public partial string? EntityLetter { get; set; }

    [ObservableProperty]
    public partial string? Detail { get; set; }

    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial DateTime CreatedOnUtc { get; set; }

    [ObservableProperty]
    public partial DateTime? ModifiedOnUtc { get; set; }

    public string Timestamp => CreatedOnUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    public string Description => Render();

    public AuditlogDisplay(Auditlog auditlog)
    {
        Id = auditlog.Id;
        UserId = auditlog.UserId;
        Action = auditlog.Action;
        EntityName = auditlog.EntityName;
        EntityLetter = auditlog.EntityLetter;
        Detail = auditlog.Detail;
        Message = auditlog.Message;
        CreatedOnUtc = auditlog.CreatedOnUtc;
        ModifiedOnUtc = auditlog.ModifiedOnUtc;
    }

    private string Render()
    {
        if (Action == AuditAction.Legacy)
        {
            return Message ?? string.Empty;
        }

        string label = DescribeEntity();

        return Action switch
        {
            AuditAction.DriveCreated => string.Format(AppResources.AuditDriveCreated, label),
            AuditAction.DriveUpdated => string.Format(AppResources.AuditDriveUpdated, label),
            AuditAction.DriveDeleted => string.Format(AppResources.AuditDriveDeleted, label),
            AuditAction.DriveDisconnected => string.Format(AppResources.AuditDriveDisconnected, label),
            AuditAction.DriveReconnected => string.Format(AppResources.AuditDriveReconnected, label),
            AuditAction.DriveReconnectFailed =>
                string.Format(AppResources.AuditDriveReconnectFailed, label, Detail ?? string.Empty),

            _ => label,
        };
    }

    private string DescribeEntity()
    {
        if (string.IsNullOrWhiteSpace(EntityName))
        {
            return string.IsNullOrWhiteSpace(EntityLetter) ? string.Empty : $"{EntityLetter}:";
        }

        return string.IsNullOrWhiteSpace(EntityLetter)
            ? EntityName
            : $"{EntityName} ({EntityLetter}:)";
    }
}
