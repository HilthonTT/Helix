using FluentAssertions;
using Helix.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace Infrastructure.UnitTests.Database;

public sealed class SqlCipherCompatibilityTests : IDisposable
{
    private const string FixtureKey = "helix-fixture-key-'quoted'";
    private const string FixtureValue = "written by bundle_e_sqlcipher 2.1.11";

    private readonly string _directory;
    private readonly string _path;

    public SqlCipherCompatibilityTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"helix tests {Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        _path = Path.Combine(_directory, "helix.db");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Database", "Fixtures", "sqlcipher-v4.db"), _path);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private static SqliteConnection Open(string dataSource, string key)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dataSource,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = key,
        }.ToString());

        connection.Open();

        return connection;
    }

    private static string? ReadFirstValue(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Fixture ORDER BY rowid LIMIT 1";

        return (string?)command.ExecuteScalar();
    }

    [Fact]
    public void ADatabaseWrittenBySqlCipher_Should_OpenThroughTheConfiguredDataSource()
    {
        using SqliteConnection connection = Open(DatabaseLocation.DataSourceFor(_path), FixtureKey);

        ReadFirstValue(connection).Should().Be(FixtureValue);
    }

    [Fact]
    public void ADatabaseWrittenBySqlCipher_Should_StayWritableAndReadable()
    {
        using (SqliteConnection connection = Open(DatabaseLocation.DataSourceFor(_path), FixtureKey))
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Fixture VALUES ('written by sqlite3mc')";
            command.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();

        using SqliteConnection reopened = Open(DatabaseLocation.DataSourceFor(_path), FixtureKey);
        using SqliteCommand count = reopened.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM Fixture";

        ((long)count.ExecuteScalar()!).Should().Be(2);
    }

    [Fact]
    public void ADatabaseWrittenBySqlCipher_Should_NotOpenWithoutTheCipherParameters()
    {
        Action open = () =>
        {
            using SqliteConnection connection = Open(_path, FixtureKey);
            ReadFirstValue(connection);
        };

        open.Should().Throw<SqliteException>();
    }

    [Fact]
    public void ADatabaseWrittenBySqlCipher_Should_NotOpenWithTheWrongKey()
    {
        Action open = () =>
        {
            using SqliteConnection connection = Open(DatabaseLocation.DataSourceFor(_path), "not-the-key");
            ReadFirstValue(connection);
        };

        open.Should().Throw<SqliteException>();
    }
}
