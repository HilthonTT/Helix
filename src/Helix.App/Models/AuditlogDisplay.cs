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

    /// <summary>The stored sentence, present only on entries predating the structured log.</summary>
    [ObservableProperty]
    public partial string? Message { get; set; }

    [ObservableProperty]
    public partial DateTime CreatedOnUtc { get; set; }

    [ObservableProperty]
    public partial DateTime? ModifiedOnUtc { get; set; }

    /// <summary>Pre-formatted stamp so the row template stays pure XAML bindings.</summary>
    public string Timestamp => CreatedOnUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");

    /// <summary>
    /// The sentence shown in the row, composed here in the user's own language.
    /// </summary>
    /// <remarks>
    /// This is the point of storing the action rather than the prose: the same entry
    /// reads as English or Japanese depending on who is signed in, and switching language
    /// re-renders the whole page rather than leaving a log frozen in whatever language it
    /// happened to be written in.
    /// </remarks>
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
        // Entries written before the log was structured have their sentence and nothing
        // else. Shown verbatim: there is nothing to compose them from, and rewriting them
        // would misrepresent what was actually recorded.
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

            // A row from a newer version than this build knows about. The timestamp and
            // the drive are still worth showing; inventing a sentence for it is not.
            _ => label,
        };
    }

    /// <summary>
    /// "Media (Z:)" — the drive as it was named at the time, which is what the stored
    /// fields hold.
    /// </summary>
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
