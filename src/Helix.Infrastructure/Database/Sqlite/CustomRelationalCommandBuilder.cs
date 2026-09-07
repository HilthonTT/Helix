using Microsoft.EntityFrameworkCore.Storage;

namespace Helix.Infrastructure.Database.Sqlite;

internal sealed class CustomRelationalCommandBuilder : RelationalCommandBuilder
{
    private const string BeginCreateTable = "CREATE TABLE";
    private const string EndCreateTable = ");";
    private const string WithoutRowId = ") WITHOUT ROWID;";

    public CustomRelationalCommandBuilder(RelationalCommandBuilderDependencies dependencies)
        : base(dependencies)
    {
    }

    public override IRelationalCommand Build()
    {
        IRelationalCommand command = base.Build();

        return new RelationalCommand(
            Dependencies,
            FixCreateTableCommand(command.CommandText),
            FixCreateTableCommand(command.LogCommandText),
            command.Parameters);
    }

    private static string FixCreateTableCommand(string originalCommandText)
    {
        int startCreateTableIndex = originalCommandText.IndexOf(BeginCreateTable);

        if (startCreateTableIndex < 0 ||
            originalCommandText.Contains(WithoutRowId) ||
            originalCommandText.Contains("AUTOINCREMENT"))
        {
            return originalCommandText;
        }

        int endCreateTableIndex = originalCommandText.IndexOf(EndCreateTable, startCreateTableIndex);

        string createTableSubstring = originalCommandText.Substring(
            startCreateTableIndex, endCreateTableIndex - startCreateTableIndex + EndCreateTable.Length);

        string newCreateTableSubstring = createTableSubstring.Replace(EndCreateTable, WithoutRowId);

        string newCommandText = originalCommandText.Replace(createTableSubstring, newCreateTableSubstring);

        return newCommandText;
    }
}
