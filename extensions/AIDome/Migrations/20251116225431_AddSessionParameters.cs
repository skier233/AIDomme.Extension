using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionParameters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Denial",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Intensity",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Kink",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Length",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Mean",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Denial",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Intensity",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Kink",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Length",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Mean",
                table: "Sessions");
        }
    }
}
