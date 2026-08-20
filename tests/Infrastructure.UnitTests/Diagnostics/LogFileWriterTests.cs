using FluentAssertions;
using Helix.Infrastructure.Diagnostics;

namespace Infrastructure.UnitTests.Diagnostics;

/// <summary>
/// Covers the file half of the log: that it writes, that it keeps the file readable
/// while it holds it open, and that it survives a directory it cannot use.
/// </summary>
public sealed class LogFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"helix-log-tests-{Guid.CreateVersion7():N}");

    private LogFileWriter Create(int retainedDays = 14) => new(() => _directory, retainedDays);

    [Fact]
    public void Write_Should_CreateTheDirectoryAndTheFile()
    {
        using LogFileWriter writer = Create();

        writer.Write("hello");

        writer.GetFiles().Should().ContainSingle();
    }

    [Fact]
    public void Write_Should_AppendEveryLine()
    {
        using LogFileWriter writer = Create();

        writer.Write("first");
        writer.Write("second");

        string file = writer.GetFiles().Single();

        // Read while the writer still holds the file open, the way the diagnostics export
        // does. FileShare.ReadWrite on this side is required: the reader's own share mode
        // has to permit the writer's existing write handle.
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        reader.ReadToEnd().Should().Contain("first").And.Contain("second");
    }

    /// <summary>
    /// The directory is resolved on first use rather than in the constructor, so that
    /// building the container never depends on MAUI being up.
    /// </summary>
    [Fact]
    public void Construction_Should_NotTouchTheDirectoryFactory()
    {
        var called = false;

        using var writer = new LogFileWriter(
            () =>
            {
                called = true;
                return _directory;
            },
            retainedDays: 14);

        called.Should().BeFalse();

        writer.Write("now it is needed");

        called.Should().BeTrue();
    }

    [Fact]
    public void Write_Should_GiveUpQuietly_WhenTheDirectoryCannotBeUsed()
    {
        // A logger that throws would take down the very code that was trying to report
        // a problem, so a broken destination has to fail silently.
        using var writer = new LogFileWriter(
            () => throw new InvalidOperationException("no app data directory here"),
            retainedDays: 14);

        Action write = () => writer.Write("anything");

        write.Should().NotThrow();
        writer.GetFiles().Should().BeEmpty();
    }

    [Fact]
    public void PruneOldFiles_Should_DeleteFilesPastTheRetentionWindow()
    {
        using LogFileWriter writer = Create(retainedDays: 7);

        // Seed a file that is older than the window, then write so a prune runs.
        Directory.CreateDirectory(_directory);

        string stale = Path.Combine(_directory, "helix-20200101.log");
        File.WriteAllText(stale, "ancient");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-30));

        writer.Write("today");

        File.Exists(stale).Should().BeFalse();
        writer.GetFiles().Should().ContainSingle();
    }

    [Fact]
    public void PruneOldFiles_Should_KeepEverything_WhenRetentionIsZero()
    {
        using LogFileWriter writer = Create(retainedDays: 0);

        Directory.CreateDirectory(_directory);

        string stale = Path.Combine(_directory, "helix-20200101.log");
        File.WriteAllText(stale, "ancient");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-3000));

        writer.Write("today");

        File.Exists(stale).Should().BeTrue();
    }

    [Fact]
    public void GetFiles_Should_IgnoreUnrelatedFiles()
    {
        using LogFileWriter writer = Create();

        writer.Write("mine");

        File.WriteAllText(Path.Combine(_directory, "notes.txt"), "not a log");

        writer.GetFiles().Should().ContainSingle().Which.Should().EndWith(".log");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception)
        {
            // A temp directory left behind is not worth failing a test run over.
        }
    }
}
