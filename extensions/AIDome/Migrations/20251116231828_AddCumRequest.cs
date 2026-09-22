using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddCumRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Mean",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Length",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Kink",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Intensity",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Denial",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: -1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CumRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Decision = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CumRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CumRequests_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CumRequests_SessionId",
                table: "CumRequests",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CumRequests_TimestampUtc",
                table: "CumRequests",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CumRequests");

            migrationBuilder.AlterColumn<int>(
                name: "Mean",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: -1);

            migrationBuilder.AlterColumn<int>(
                name: "Length",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: -1);

            migrationBuilder.AlterColumn<int>(
                name: "Kink",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: -1);

            migrationBuilder.AlterColumn<int>(
                name: "Intensity",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: -1);

            migrationBuilder.AlterColumn<int>(
                name: "Denial",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: -1);
        }
    }
}
