using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Host_AutoConnect_And_Persistent_To_Drive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IpAddress",
                table: "Drives",
                newName: "Host");

            // True, not the scaffolded false: every drive that already exists was created
            // when auto-connect was a single user-level setting and applied to all of
            // them. Backfilling false would quietly opt every one of them out of the
            // startup pass and the watchdog on the first run after the upgrade.
            migrationBuilder.AddColumn<bool>(
                name: "AutoConnect",
                table: "Drives",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "Persistent",
                table: "Drives",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConnect",
                table: "Drives");

            migrationBuilder.DropColumn(
                name: "Persistent",
                table: "Drives");

            migrationBuilder.RenameColumn(
                name: "Host",
                table: "Drives",
                newName: "IpAddress");
        }
    }
}
