using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class DropInstructionStepUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstructionSteps_SessionId_Ordinal",
                table: "InstructionSteps");

            migrationBuilder.CreateIndex(
                name: "IX_InstructionSteps_SessionId_Ordinal",
                table: "InstructionSteps",
                columns: new[] { "SessionId", "Ordinal" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstructionSteps_SessionId_Ordinal",
                table: "InstructionSteps");

            migrationBuilder.CreateIndex(
                name: "IX_InstructionSteps_SessionId_Ordinal",
                table: "InstructionSteps",
                columns: new[] { "SessionId", "Ordinal" },
                unique: true);
        }
    }
}
