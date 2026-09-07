using System.Text;

namespace Helix.Infrastructure.Diagnostics;

internal sealed class LogFileWriter : IDisposable
{
    private const long MaximumFileBytes = 2 * 1024 * 1024;

    private const string FilePrefix = "helix-";
    private const string FileExtension = ".log";

    private readonly Func<string> _directoryFactory;

    private readonly int _retainedDays;
    private readonly Lock _gate = new();

    private string? _directory;

    private StreamWriter? _writer;
    private DateOnly _openFor;
    private int _sequence;
    private bool _disposed;

    private bool _broken;

    public LogFileWriter(Func<string> directoryFactory, int retainedDays)
    {
        _directoryFactory = directoryFactory;
        _retainedDays = retainedDays;
    }

    public string DirectoryPath
    {
        get
        {
            try
            {
                return ResolveDirectory();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }

    private string ResolveDirectory() => _directory ??= _directoryFactory();

    public void Write(string line)
    {
        if (_disposed || _broken)
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                StreamWriter writer = EnsureWriter();

                writer.WriteLine(line);
                writer.Flush();
            }
            catch (Exception)
            {
                _broken = true;

                CloseWriter();
            }
        }
    }

    public IReadOnlyList<string> GetFiles()
    {
        try
        {
            string directory = ResolveDirectory();

            if (!Directory.Exists(directory))
            {
                return [];
            }

            return [.. Directory
                .EnumerateFiles(directory, $"{FilePrefix}*{FileExtension}")
                .OrderByDescending(File.GetLastWriteTimeUtc)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            try
            {
                _writer?.Flush();
            }
            catch (Exception)
            {
            }
        }
    }

    private StreamWriter EnsureWriter()
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (_writer is not null && _openFor == today && CurrentLength() < MaximumFileBytes)
        {
            return _writer;
        }

        if (_openFor != today)
        {
            _sequence = 0;
        }
        else if (_writer is not null)
        {
            _sequence++;
        }

        CloseWriter();

        string directory = ResolveDirectory();

        Directory.CreateDirectory(directory);

        _openFor = today;

        string suffix = _sequence == 0 ? string.Empty : $"-{_sequence}";
        string path = Path.Combine(directory, $"{FilePrefix}{today:yyyyMMdd}{suffix}{FileExtension}");

        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        PruneOldFiles();

        return _writer;
    }

    private long CurrentLength()
    {
        try
        {
            return _writer?.BaseStream.Length ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch (Exception)
        {
        }

        _writer = null;
    }

    private void PruneOldFiles()
    {
        if (_retainedDays <= 0)
        {
            return;
        }

        try
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-_retainedDays);

            foreach (string file in Directory.EnumerateFiles(ResolveDirectory(), $"{FilePrefix}*{FileExtension}"))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_gate)
        {
            CloseWriter();
        }
    }
}
