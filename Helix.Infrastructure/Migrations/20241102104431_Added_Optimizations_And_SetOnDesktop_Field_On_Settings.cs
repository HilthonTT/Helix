using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Optimizations_And_SetOnDesktop_Field_On_Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SetDesktopShortcut",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Users_CreatedOnUtc",
                table: "Users",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Drives_CreatedOnUtc",
                table: "Drives",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Drives_Letter",
                table: "Drives",
                column: "Letter");

            migrationBuilder.CreateIndex(
                name: "IX_Drives_Name",
                table: "Drives",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedOnUtc",
                table: "AuditLogs",
                column: "CreatedOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_CreatedOnUtc",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Drives_CreatedOnUtc",
                table: "Drives");

            migrationBuilder.DropIndex(
                name: "IX_Drives_Letter",
                table: "Drives");

            migrationBuilder.DropIndex(
                name: "IX_Drives_Name",
                table: "Drives");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CreatedOnUtc",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "SetDesktopShortcut",
                table: "Settings");
        }
    }
}
