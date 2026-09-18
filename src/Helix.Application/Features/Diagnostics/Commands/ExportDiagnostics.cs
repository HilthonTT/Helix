using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Diagnostics;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Users;

namespace Helix.Application.Features.Diagnostics.Commands;

public sealed class ExportDiagnostics(
    ILoggedInUser loggedInUser,
    IFolderPicker folderPicker,
    IDiagnosticsLog diagnosticsLog) : IHandler
{
    public async Task<Result<string>> Handle(CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        FolderPick folder = await folderPicker.PickAsync(cancellationToken);
        if (!folder.IsSuccessful)
        {
            return Result.Failure<string>(FolderPickerErrors.Cancelled);
        }

        if (string.IsNullOrWhiteSpace(folder.Path))
        {
            return Result.Failure<string>(FolderPickerErrors.InvalidFolderPath);
        }

        return await diagnosticsLog.ExportAsync(folder.Path, cancellationToken);
    }
}
