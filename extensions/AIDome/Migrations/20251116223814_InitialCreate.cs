using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastInteractionUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StimMethods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TortLevel = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Intensity = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Tease = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StimMethods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orgasms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Allowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orgasms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orgasms_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Edges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StimMethodId = table.Column<int>(type: "INTEGER", nullable: false),
                    Speed = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    HeldDuration = table.Column<double>(type: "REAL", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Edges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Edges_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Edges_StimMethods_StimMethodId",
                        column: x => x.StimMethodId,
                        principalTable: "StimMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Intensity", "Name", "Tease", "TortLevel" },
                values: new object[] { 1, 5, "Default", 2, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Edges_SessionId",
                table: "Edges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Edges_StimMethodId",
                table: "Edges",
                column: "StimMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Edges_TimestampUtc",
                table: "Edges",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Orgasms_SessionId",
                table: "Orgasms",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Orgasms_TimestampUtc",
                table: "Orgasms",
                column: "TimestampUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Edges");

            migrationBuilder.DropTable(
                name: "Orgasms");

            migrationBuilder.DropTable(
                name: "StimMethods");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
