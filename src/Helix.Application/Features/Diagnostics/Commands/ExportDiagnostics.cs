using CommunityToolkit.Maui.Storage;
using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Diagnostics;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;
using System.Runtime.Versioning;

namespace Helix.Application.Features.Diagnostics.Commands;

/// <summary>
/// Writes the log files into a zip the user can attach to a bug report.
/// </summary>
public sealed class ExportDiagnostics(
    ILoggedInUser loggedInUser,
    IDiagnosticsLog diagnosticsLog) : IHandler
{
    // FolderPicker is not available on every platform this assembly's TFM allows, and
    // Helix only ever runs on desktop, so the call site is narrowed to match — the same
    // treatment ExportDrives gets.
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("maccatalyst14.0")]
    public async Task<Result<string>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        FolderPickerResult folderResult = await FolderPicker.Default.PickAsync(cancellationToken);
        if (!folderResult.IsSuccessful)
        {
            return Result.Failure<string>(FolderPickerErrors.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(folderResult.Folder?.Path))
        {
            return Result.Failure<string>(FolderPickerErrors.InvalidFolderPath);
        }

        return await diagnosticsLog.ExportAsync(folderResult.Folder.Path, cancellationToken);
    }
}
