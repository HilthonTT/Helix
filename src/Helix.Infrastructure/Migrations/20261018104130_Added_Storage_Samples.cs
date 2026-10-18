using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Helix.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Added_Storage_Samples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StorageSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VolumeId = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    FreeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Letters = table.Column<string>(type: "TEXT", nullable: false),
                    RecordedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorageSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorageSamples_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StorageSamples_UserId_Day",
                table: "StorageSamples",
                columns: new[] { "UserId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_StorageSamples_UserId_VolumeId_Day",
                table: "StorageSamples",
                columns: new[] { "UserId", "VolumeId", "Day" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StorageSamples");
        }
    }
}
