using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    public partial class Added_Host_AutoConnect_And_Persistent_To_Drive : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IpAddress",
                table: "Drives",
                newName: "Host");

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
