using SharedKernel;

namespace Helix.Application.Core.Errors;

internal static class FolderPickerErrors
{
    public static readonly Error Cancelled = Error.Problem(
        "FolderPicker.Cancelled",
        "You've cancelled this operation.");

    public static readonly Error InvalidFolderPath = Error.Problem(
        "FolderPicker.InvalidFolderPath",
        "This folder path is invalid, please choose a diffeent one.");

    public static readonly Error FileAlreadyExists = Error.Problem(
        "FolderPicker.FileAlreadyExists",
        "The file already exists, please delete it first.");

    public static readonly Error UnauthorizedFileAccess = Error.Problem(
        "FolderPicker.UnauthorizedFileAccess",
        "You don't have the necessary permissions to access this file or folder. Please check your permissions and try again.");
}
