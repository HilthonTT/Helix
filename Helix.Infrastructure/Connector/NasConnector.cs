using Helix.Application.Abstractions.Connector;
using Helix.Domain.Drives;
using SharedKernel;
using System.Diagnostics;
using System.Text;

namespace Helix.Infrastructure.Connector;

internal sealed class NasConnector : INasConnector
{
    public async Task<Result> ConnectAsync(Drive drive, CancellationToken cancellationToken = default)
    {
        try
        {
            string arguments = CreateConnectArguments(drive);
            return await ExecuteProcessAsync(arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure(DriveErrors.FailedToConnect($"Unexpected error: {ex.Message}"));
        }
    }

    public async Task<Result> DisconnectAsync(Drive drive, CancellationToken cancellationToken = default)
    {
        try
        {
            string arguments = CreateDisconnectArguments(drive);
            return await ExecuteProcessAsync(arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result.Failure(DriveErrors.FailedToDisconnect($"Unexpected error: {ex.Message}"));
        }
    }

    public bool IsConnected(string letter) =>
        DriveInfo
        .GetDrives()
        .Any(d => d.Name.StartsWith($"{letter}:\\", StringComparison.OrdinalIgnoreCase));
    

    private static string CreateConnectArguments(Drive drive)
    {
        var stringBuilder = new StringBuilder()
            .Append($"use {drive.Letter}: ")
            .Append($"\"\\\\{drive.IpAddress}\\{drive.Name}\" ")
            .Append($"{drive.Password} ")
            .Append($"/user:{drive.Username} ")
            .Append("/persistent:no");

        return stringBuilder.ToString();
    }

    private static string CreateDisconnectArguments(Drive drive) => $"use {drive.Letter}: /del";

    private static Process CreateProcess(string arguments)
    {
        const string command = "net";

        return new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            }
        };
    }

    private static async Task<Result> ExecuteProcessAsync(string arguments, CancellationToken cancellationToken)
    {
        using Process process = CreateProcess(arguments);

        try
        {
            string error = string.Empty;

            if (!process.Start())
            {
                error = await process.StandardError.ReadToEndAsync(cancellationToken);
                return Result.Failure(DriveErrors.FailedToConnect($"Process failed to start: {error}"));
            }

            await process.WaitForExitAsync(cancellationToken);

            string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);

            error = await process.StandardError.ReadToEndAsync(cancellationToken);

            return process.ExitCode == 0
                ? Result.Success()
                : Result.Failure(DriveErrors.FailedToConnect(error));
        }
        catch (OperationCanceledException)
        {
            process.Kill();
            return Result.Failure(DriveErrors.FailedToConnect("Operation canceled by user."));
        }
        catch (Exception ex)
        {
            return Result.Failure(DriveErrors.FailedToConnect($"Unexpected error: {ex.Message}"));
        }
    }
}
