using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    public partial class Added_Storage_Alert_Threshold_To_Settings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StorageAlertThresholdPercent",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StorageAlertThresholdPercent",
                table: "Settings");
        }
    }
}
