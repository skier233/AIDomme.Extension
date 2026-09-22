using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class ExpandSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TortLevel",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 1);

            migrationBuilder.AlterColumn<int>(
                name: "Tease",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Closeness",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "StimMethods",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RequiresLube",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ToyCategory",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAnal",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowAutoblow",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowBondage",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowCEI",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowFleshlight",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowProstate",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowToys",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllowVibrator",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "GameChallenges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    GameType = table.Column<int>(type: "INTEGER", nullable: false),
                    CorrectAnswer = table.Column<int>(type: "INTEGER", nullable: false),
                    UserAnswer = table.Column<int>(type: "INTEGER", nullable: true),
                    Won = table.Column<bool>(type: "INTEGER", nullable: true),
                    Prompt = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, defaultValue: ""),
                    FailureConsequence = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, defaultValue: ""),
                    SuccessReward = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, defaultValue: ""),
                    AnsweredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GameChallenges_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Infractions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, defaultValue: ""),
                    PunishmentAssigned = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Infractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Infractions_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InstructionSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    StimMethodId = table.Column<int>(type: "INTEGER", nullable: true),
                    Speed = table.Column<int>(type: "INTEGER", nullable: false),
                    Tenseness = table.Column<int>(type: "INTEGER", nullable: false),
                    DurationOrCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, defaultValue: ""),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstructionSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstructionSteps_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstructionSteps_StimMethods_StimMethodId",
                        column: x => x.StimMethodId,
                        principalTable: "StimMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Punishments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Tier = table.Column<int>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false, defaultValue: ""),
                    RemainingCount = table.Column<int>(type: "INTEGER", nullable: false),
                    DenialEndsAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Punishments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Punishments_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[] { 7, "Perform the figure eight on your cock.", 6, "Figure Eight", true, 0, 4 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "TortLevel" },
                values: new object[] { 2, 3, "Stroke your cock from under your leg.", 4, "Stroke From Under (dry)", 1 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[,]
                {
                    { 3, 6, "Stroke your cock from under your leg.", 6, "Stroke From Under (lube)", true, 3 },
                    { 4, 1, "Palm your cock.", 9, "Palm", true, 9 },
                    { 5, 2, "Perform worse than palming on your cock.", 10, "Worse Than Palm", true, 10 },
                    { 6, 4, "Place one hand palm down on the top of your cock and the other lengthwise on the bottom and wrap the fingers of the top hand around. Then stroke.", 5, "Top-Bottom Grip", true, 2 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "Tease" },
                values: new object[] { 7, 3, "Roll your cock between your hands.", 3, "Roll Between Hands", 3 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease" },
                values: new object[,]
                {
                    { 8, 3, "Rub your cock back and forth across your stomach.", 4, "Stomach Rub", true, 7 },
                    { 9, 4, "Push your cock against your stomach and then move your palm up and down on top of it.", 5, "Stomach Palm", true, 6 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "TortLevel" },
                values: new object[] { 10, 3, "Stroke up and down your cock with 1 hand.", 4, "1-Hand Stroke (dry)", 1 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[,]
                {
                    { 11, 5, "Stroke up and down your cock with 1 hand.", 6, "1-Hand Stroke (lube)", true, 3 },
                    { 12, 8, "Stroke up and down your cock with 2 hands.", 7, "2-Hand Stroke", true, 2 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[,]
                {
                    { 13, 6, "Stroke up and down the head of your cock.", 6, "Head Stroke", true, 2, 4 },
                    { 14, 3, "Stroke up and down the tip of your cock.", 7, "Tip Stroke", true, 4, 6 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[] { 15, 9, "Hold down the skin of your cock and stroke the shaft with the other hand.", 8, "Skin-Held Shaft Stroke", true, 3 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[,]
                {
                    { 16, 7, "Hold down the skin of your cock and stroke the head with the other hand.", 7, "Skin-Held Head Stroke", true, 2, 5 },
                    { 17, 4, "Hold down the skin of your cock and stroke the tip with the other hand.", 8, "Skin-Held Tip Stroke", true, 4, 7 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "Tease" },
                values: new object[] { 18, 7, "Rub/tickle the sweet spot of your cock.", 3, "Sweet Spot Rub", 3 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[] { 19, 8, "Twist in opposite directions on your cock.", 7, "Twist Opposite", true, 2 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[,]
                {
                    { 20, 6, "Twist the head of your cock.", 6, "Twist Head", true, 2, 4 },
                    { 21, 3, "Twist the tip of your cock.", 7, "Twist Tip", true, 4, 6 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[] { 22, 9, "Hold down the skin of your cock and twist the shaft with the other hand.", 8, "Skin-Held Shaft Twist", true, 3 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[,]
                {
                    { 23, 7, "Hold down the skin of your cock and twist the head with the other hand.", 7, "Skin-Held Head Twist", true, 2, 5 },
                    { 24, 4, "Hold down the skin of your cock and twist the tip with the other hand.", 8, "Skin-Held Tip Twist", true, 4, 7 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "TortLevel" },
                values: new object[] { 25, 5, "Only stroke down/up your cock.", 8, "One-Direction Stroke", true, 5 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel" },
                values: new object[,]
                {
                    { 26, 3, "Only stroke down/up your cock using your fingers in a circle.", 5, "One-Direction Finger Circle", true, 6, 3 },
                    { 27, 2, "Twist around the bottom of the tip of your cock.", 3, "Tip Base Twist", true, 6, 3 },
                    { 28, 1, "Make a fist and peek the head of your cock into it.", 10, "Fist Peek", true, 5, 10 },
                    { 29, 1, "Tease the head of your cock with fingers in almost a pinching motion.", 9, "Head Pinch Tease", true, 6, 9 }
                });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Description", "Intensity", "Name", "Tease", "TortLevel" },
                values: new object[] { 30, "Move one finger up and down your cock gently.", 1, "Single Finger Tease", 9, 1 });

            migrationBuilder.InsertData(
                table: "StimMethods",
                columns: new[] { "Id", "Closeness", "Description", "Intensity", "Name", "RequiresLube", "Tease", "TortLevel", "ToyCategory" },
                values: new object[,]
                {
                    { 31, 2, "Use the penis head vibrator at a slow speed.", 4, "Head Vibrator (slow)", true, 6, 3, 1 },
                    { 32, 4, "Use the penis head vibrator at a medium speed.", 6, "Head Vibrator (medium)", true, 3, 5, 1 },
                    { 33, 8, "Use the penis head vibrator at max speed.", 8, "Head Vibrator (max)", true, 2, 7, 1 },
                    { 34, 7, "Use the fleshlight at slow speed.", 6, "Fleshlight (slow)", true, 4, 5, 2 },
                    { 35, 6, "Use the fleshlight at medium speed.", 5, "Fleshlight (medium)", true, 5, 4, 2 },
                    { 36, 8, "Use the fleshlight at high speed.", 7, "Fleshlight (fast)", true, 3, 3, 2 },
                    { 37, 4, "Use the autoblow at low speed.", 6, "Autoblow (slow)", true, 6, 7, 3 },
                    { 38, 5, "Use the autoblow at medium speed.", 6, "Autoblow (medium)", true, 3, 5, 3 },
                    { 39, 6, "Use the autoblow at high speed.", 7, "Autoblow (fast)", true, 2, 3, 3 },
                    { 40, 4, "Use the prostate massager to intensify stimulation.", 6, "Prostate Massager", true, 7, 5, 4 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameChallenges_IssuedAtUtc",
                table: "GameChallenges",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GameChallenges_SessionId",
                table: "GameChallenges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Infractions_SessionId",
                table: "Infractions",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Infractions_TimestampUtc",
                table: "Infractions",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InstructionSteps_SessionId_Ordinal",
                table: "InstructionSteps",
                columns: new[] { "SessionId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstructionSteps_StimMethodId",
                table: "InstructionSteps",
                column: "StimMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_Punishments_IssuedAtUtc",
                table: "Punishments",
                column: "IssuedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Punishments_SessionId",
                table: "Punishments",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GameChallenges");

            migrationBuilder.DropTable(
                name: "Infractions");

            migrationBuilder.DropTable(
                name: "InstructionSteps");

            migrationBuilder.DropTable(
                name: "Punishments");

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DropColumn(
                name: "Closeness",
                table: "StimMethods");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "StimMethods");

            migrationBuilder.DropColumn(
                name: "RequiresLube",
                table: "StimMethods");

            migrationBuilder.DropColumn(
                name: "ToyCategory",
                table: "StimMethods");

            migrationBuilder.DropColumn(
                name: "AllowAnal",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowAutoblow",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowBondage",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowCEI",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowFleshlight",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowProstate",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowToys",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "AllowVibrator",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "Sessions");

            migrationBuilder.AlterColumn<int>(
                name: "TortLevel",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<int>(
                name: "Tease",
                table: "StimMethods",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldDefaultValue: 0);

            migrationBuilder.UpdateData(
                table: "StimMethods",
                keyColumn: "Id",
                keyValue: 1,
                columns: new[] { "Intensity", "Name", "Tease", "TortLevel" },
                values: new object[] { 5, "Default", 2, 1 });
        }
    }
}
