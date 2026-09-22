using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionAntiCheatAndTuningRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDebugSession",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SuspectedUnauthorizedOrgasm",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TuningRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CandidateKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Group = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, defaultValue: ""),
                    Title = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false, defaultValue: ""),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, defaultValue: ""),
                    SeedRating = table.Column<double>(type: "REAL", nullable: false, defaultValue: 1000.0),
                    Rating = table.Column<double>(type: "REAL", nullable: false, defaultValue: 1000.0),
                    Wins = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    Losses = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TuningRatings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TuningRatings_CandidateKey",
                table: "TuningRatings",
                column: "CandidateKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TuningRatings");

            migrationBuilder.DropColumn(
                name: "IsDebugSession",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "SuspectedUnauthorizedOrgasm",
                table: "Sessions");
        }
    }
}
