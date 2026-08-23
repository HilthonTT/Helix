using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Storage_Alert_Threshold_To_Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ten, not zero, so an install that already exists gets the warning rather
            // than having to find the setting to be told its pool is nearly full. The
            // audit-log retention column above defaulted to its inert value on purpose —
            // that one deletes rows, and turning deletion on under someone is not the
            // same as telling them something. Zero here means "never warn me", and the
            // settings page is where it is turned off.
            migrationBuilder.AddColumn<int>(
                name: "StorageAlertThresholdPercent",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageAlertThresholdPercent",
                table: "Settings");
        }
    }
}
