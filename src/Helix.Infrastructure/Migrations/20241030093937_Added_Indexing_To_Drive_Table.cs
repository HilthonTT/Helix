using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    public partial class Added_Indexing_To_Drive_Table : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Drives_Name_Letter",
                table: "Drives",
                columns: new[] { "Name", "Letter" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drives_Name_Letter",
                table: "Drives");
        }
    }
}
