using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditlogUserDateIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_CreatedOnUtc",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId_CreatedOnUtc",
                table: "AuditLogs",
                columns: new[] { "UserId", "CreatedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_UserId_CreatedOnUtc",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedOnUtc",
                table: "AuditLogs",
                column: "CreatedOnUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");
        }
    }
}
