using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    public partial class Added_Home_Network_And_Global_Hotkeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GlobalHotkeys",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HomeNetworkId",
                table: "Drives",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HomeNetworkName",
                table: "Drives",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GlobalHotkeys",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "HomeNetworkId",
                table: "Drives");

            migrationBuilder.DropColumn(
                name: "HomeNetworkName",
                table: "Drives");
        }
    }
}
