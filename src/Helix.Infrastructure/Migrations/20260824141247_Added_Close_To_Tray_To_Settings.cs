using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    public partial class Added_Close_To_Tray_To_Settings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
