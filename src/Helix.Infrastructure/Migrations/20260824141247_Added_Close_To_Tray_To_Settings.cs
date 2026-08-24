using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Close_To_Tray_To_Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // True on both, not the scaffolded false: every install that already exists
            // has had the close button hide to the tray and say so since there was a
            // tray, and backfilling false would turn both off on the first run after the
            // upgrade — the second of them silently, and the first by quitting the app
            // the next time somebody closed the window, taking the watchdog with it.
            migrationBuilder.AddColumn<bool>(
                name: "CloseToTray",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnMinimizeToTray",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseToTray",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyOnMinimizeToTray",
                table: "Settings");
        }
    }
}
