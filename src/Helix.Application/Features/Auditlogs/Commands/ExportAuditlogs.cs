using Helix.Application.Abstractions.Authentication;
using Helix.Application.Abstractions.Desktop;
using Helix.Application.Abstractions.Handlers;
using Helix.Application.Core.Errors;
using Helix.Domain.Auditlogs;
using Helix.Domain.Users;
using System.Globalization;
using System.Text;

namespace Helix.Application.Features.Auditlogs.Commands;

public sealed class ExportAuditlogs(
    IAuditlogRepository auditlogRepository,
    ILoggedInUser loggedInUser,
    IFolderPicker folderPicker,
    IDateTimeProvider dateTimeProvider) : IHandler
{
    private const string FileExtension = ".csv";

    public sealed record Request(IReadOnlyList<string> Headers, Func<Auditlog, string> Describe);

    public async Task<Result<string>> Handle(Request request, CancellationToken cancellationToken = default)
    {
        if (!loggedInUser.IsLoggedIn)
        {
            return Result.Failure<string>(AuthenticationErrors.InvalidPermissions);
        }

        List<Auditlog> rows = await auditlogRepository.GetPageAsNoTrackingAsync(
            loggedInUser.UserId,
            skip: 0,
            take: int.MaxValue,
            oldestFirst: true,
            cancellationToken);

        if (rows.Count == 0)
        {
            return Result.Failure<string>(AuditlogErrors.NothingToExport);
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

        string fileName = $"helix-history-{dateTimeProvider.UtcNow:yyyyMMdd-HHmmss}{FileExtension}";
        string filePath = Path.Combine(folder.Path, fileName);

        if (File.Exists(filePath))
        {
            return Result.Failure<string>(FolderPickerErrors.FileAlreadyExists);
        }

        string csv = Compose(rows, request);

        try
        {
            await File.WriteAllTextAsync(
                filePath,
                csv,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Result.Failure<string>(FolderPickerErrors.UnauthorizedFileAccess);
        }
        catch (IOException ex)
        {
            return Result.Failure<string>(FolderPickerErrors.WriteFailed(ex.Message));
        }

        return filePath;
    }

    internal static string Compose(IEnumerable<Auditlog> rows, Request request)
    {
        var builder = new StringBuilder();

        AppendLine(builder, request.Headers);

        foreach (Auditlog row in rows)
        {
            AppendLine(builder,
            [
                row.CreatedOnUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                row.Action.ToString(),
                row.EntityLetter ?? string.Empty,
                row.EntityName ?? string.Empty,
                request.Describe(row),
            ]);
        }

        return builder.ToString();
    }

    private static void AppendLine(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(fields[i]));
        }

        builder.Append("\r\n");
    }

    internal static string Escape(string value)
    {
        string safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? $"'{value}"
            : value;

        return safe.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : safe;
    }
}
