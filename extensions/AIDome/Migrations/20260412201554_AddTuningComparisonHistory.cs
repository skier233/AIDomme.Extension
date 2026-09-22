using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddTuningComparisonHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TuningComparisonHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WinnerKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    LoserKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TuningComparisonHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TuningComparisonHistory_Mode_LoserKey",
                table: "TuningComparisonHistory",
                columns: new[] { "Mode", "LoserKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TuningComparisonHistory_Mode_WinnerKey",
                table: "TuningComparisonHistory",
                columns: new[] { "Mode", "WinnerKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TuningComparisonHistory");
        }
    }
}
