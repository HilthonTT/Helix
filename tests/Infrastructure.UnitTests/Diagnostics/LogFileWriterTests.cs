using FluentAssertions;
using Helix.Infrastructure.Diagnostics;

namespace Infrastructure.UnitTests.Diagnostics;

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

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        reader.ReadToEnd().Should().Contain("first").And.Contain("second");
    }

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
        }
    }
}
