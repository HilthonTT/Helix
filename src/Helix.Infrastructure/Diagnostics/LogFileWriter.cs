using System.Text;

namespace Helix.Infrastructure.Diagnostics;

/// <summary>
/// Appends log lines to a dated file, rolling on size and deleting old ones.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from a logging package, for the same reason the rest
/// of this project keeps its dependency list short: what is needed here is one text file
/// per day with a cap on it, and every library that does that also brings sinks,
/// enrichers and a configuration system Helix has no use for.
///
/// Every write goes through one lock. Log writes come from timer callbacks, the tray's
/// message loop and the UI thread at once, and the volume — a handful of lines per drive
/// event — is nowhere near enough to justify a background queue.
/// </remarks>
internal sealed class LogFileWriter : IDisposable
{
    /// <summary>Roll to a new file past this size, so one bad day cannot fill the disk.</summary>
    private const long MaximumFileBytes = 2 * 1024 * 1024;

    private const string FilePrefix = "helix-";
    private const string FileExtension = ".log";

    /// <summary>
    /// Resolves the log directory the first time one is needed.
    /// </summary>
    /// <remarks>
    /// A factory rather than a path because the path comes from MAUI's
    /// <c>FileSystem.AppDataDirectory</c>, which is WinRT and throws outside a running
    /// app. Resolving it in the constructor made merely building the container — as the
    /// architecture and DI tests do — depend on MAUI being initialized.
    /// </remarks>
    private readonly Func<string> _directoryFactory;

    private readonly int _retainedDays;
    private readonly Lock _gate = new();

    private string? _directory;

    private StreamWriter? _writer;
    private DateOnly _openFor;
    private int _sequence;
    private bool _disposed;

    /// <summary>
    /// Set once writing has failed, so a full or read-only disk costs one failed attempt
    /// rather than one per line for the rest of the session.
    /// </summary>
    private bool _broken;

    public LogFileWriter(Func<string> directoryFactory, int retainedDays)
    {
        _directoryFactory = directoryFactory;
        _retainedDays = retainedDays;
    }

    /// <summary>
    /// Where the logs live, or empty if that cannot be worked out. Empty rather than an
    /// exception: every caller is either reporting a problem or exporting for one.
    /// </summary>
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
                // Nowhere to report this: the thing that reports problems is what just
                // failed. Give up quietly rather than throwing out of a logging call and
                // taking down whatever was being logged about.
                _broken = true;

                CloseWriter();
            }
        }
    }

    /// <summary>Returns the log files, newest first.</summary>
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

    /// <summary>
    /// Flushes and releases the current file so it can be copied.
    /// </summary>
    /// <remarks>
    /// The file is opened with <see cref="FileShare.Read"/>, so a copy would work
    /// anyway; this just makes sure the last line written is in it.
    /// </remarks>
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
                // Same reasoning as Write: a logger cannot report its own failure.
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
            // Same day, file is full — move to the next slice.
            _sequence++;
        }

        CloseWriter();

        string directory = ResolveDirectory();

        Directory.CreateDirectory(directory);

        _openFor = today;

        // The first file of a day is plain "helix-20260820.log"; only a roll adds a
        // suffix, so the common case reads as one file per day.
        string suffix = _sequence == 0 ? string.Empty : $"-{_sequence}";
        string path = Path.Combine(directory, $"{FilePrefix}{today:yyyyMMdd}{suffix}{FileExtension}");

        // Shared as widely as possible so the current day's file — the one worth having —
        // can still be read and exported while Helix holds it open. A reader has to ask
        // for FileShare.ReadWrite itself: its share mode must permit this write handle,
        // and a plain FileShare.Read open would be refused however permissive this is.
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
            // Disposing a stream over a disconnected disk can throw; nothing to do.
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
            // A file held open by something else is not worth failing a log write over.
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
