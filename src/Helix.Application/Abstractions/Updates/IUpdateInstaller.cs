namespace Helix.Application.Abstractions.Updates;

public interface IUpdateInstaller
{
    bool IsSupported { get; }

    Task<Result<string>> StageAsync(
        UpdateCheck update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Result Apply(string stagedDirectory);
}
