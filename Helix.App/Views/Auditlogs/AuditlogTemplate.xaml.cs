using Helix.App.Models;

namespace Helix.App.Views.Auditlogs;

public sealed partial class AuditlogTemplate : ContentView
{
	public AuditlogTemplate()
	{
		InitializeComponent();
	}

    protected override void OnBindingContextChanged()
    {
		if (BindingContext is not AuditlogDisplay auditlog)
		{
			return;
		}

		Message.Text = auditlog.Message;
        Date.Text = auditlog.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    }
}