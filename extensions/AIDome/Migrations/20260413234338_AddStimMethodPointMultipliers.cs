using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddStimMethodPointMultipliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "EdgePointMultiplier",
                table: "StimMethods",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TimePointMultiplier",
                table: "StimMethods",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EdgePointMultiplier",
                table: "StimMethods");

            migrationBuilder.DropColumn(
                name: "TimePointMultiplier",
                table: "StimMethods");
        }
    }
}
