using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AIDome2025.Migrations
{
    /// <inheritdoc />
    public partial class AddPunishmentEndersAndPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowSelfOral",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Points",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HoldSeconds",
                table: "InstructionSteps",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "PunishmentEnders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Anticipation = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    Humiliation = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 1),
                    RequiresBondage = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresCEI = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresSelfOral = table.Column<bool>(type: "INTEGER", nullable: false),
                    InstructionText = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false, defaultValue: ""),
                    AnnouncementText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PunishmentEnders", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "PunishmentEnders",
                columns: new[] { "Id", "AnnouncementText", "Anticipation", "Humiliation", "InstructionText", "Name", "RequiresBondage", "RequiresCEI", "RequiresSelfOral", "Severity" },
                values: new object[,]
                {
                    { 1, "You're going to cum. But you're not going to enjoy it.", 6, 3, "You are going to cum tensed at maximum speed. Do NOT slow down as you cum. Keep going at full speed through the entire orgasm.", "Intense Forced Cum", false, false, false, 7 },
                    { 2, "Open your mouth. You're going to need it.", 8, 9, "Put your cock in your mouth. Swirl your tongue around the head. Keep swirling as you cum. Swallow every drop. If anything falls, you will be punished again.", "Tongue Swirl CEI", false, true, true, 9 },
                    { 3, "I hope you're flexible. You're going to swallow every last drop.", 9, 10, "Put your cock as deep down your throat as you can. Use both hands on the lower shaft at maximum speed. Keep going as you cum. Swallow every drop as you are cumming. Not a single drop can fall.", "Self-Oral Forced Cum", false, true, true, 9 },
                    { 4, "You'll get to cum. But what happens after is going to be much worse.", 7, 4, "Cum using your normal stroke. The INSTANT you finish, switch to palming at maximum speed for 30 seconds. No stopping no matter how much it hurts.", "Post-Orgasm Torture (30s)", false, false, false, 8 },
                    { 5, "You're going to wish you never asked to cum.", 9, 5, "Cum using your normal stroke. The INSTANT you finish, switch to worse than palming at maximum speed tensed for 60 seconds. No stopping. No slowing down. No mercy.", "Post-Orgasm Torture (60s)", false, false, false, 10 },
                    { 6, "You'll get to cum. Sort of.", 5, 6, "Edge one last time. When you reach the absolute edge, let go completely. Do not touch yourself as you cum. Watch it dribble out. That's all you get.", "Ruined Orgasm", false, false, false, 6 },
                    { 7, "Get the duct tape. You're not going anywhere.", 9, 4, "Get duct tape. Tape your ankles, knees, thighs, and stomach so you cannot move. Now cum tensed at maximum speed and keep going through the entire orgasm. You can't escape it.", "Bondage Intense Cum", true, false, false, 8 },
                    { 8, "Tape yourself down. Every inch. You know what's coming.", 10, 10, "Get duct tape. Tape yourself from ankles to stomach — no skin visible. Now put your cock in your mouth and cum down your throat. Keep going as you cum. Swallow everything. If a single drop falls, the punishment doubles.", "Bondage Self-Oral CEI", true, true, true, 10 },
                    { 9, "Two chances to cum. And you won't enjoy either one.", 7, 7, "Edge. When you reach the edge, let go completely. Wait 5 seconds. Now stroke again until you reach the edge. Let go again. That's your punishment — two ruined orgasms. Nothing full.", "Double Ruined", false, false, false, 7 },
                    { 10, "Your fate is in the coin's hands now. Not mine.", 10, 7, "Flip a coin. Heads: cum tensed at maximum speed and keep going for 30 seconds after. Tails: put your cock in your mouth and cum down your throat, swallowing every drop. No re-flips.", "Coin Flip Punishment", false, true, true, 8 },
                    { 11, "Five more edges. And then? Nothing.", 8, 2, "Edge 5 more times. Slowly. Hold each edge for 10 seconds. When you're done, hands off. You don't get to cum. Walk away.", "Denied After Edge Marathon", false, false, false, 5 },
                    { 12, "Only the tip. That's all you get.", 7, 8, "Make a fist. Peek the head of your cock into it until you cum. Keep peeking for 20 seconds after you cum. No full strokes allowed.", "Fist-Peek Torture Cum", false, false, false, 9 },
                    { 13, "Only the head. Maximum speed. No mercy.", 6, 6, "Stroke only the head of your cock at maximum speed until you cum. Do not touch the shaft at all. Keep stroking the head for 15 seconds after you finish.", "Head-Only Speed Cum", false, false, false, 7 },
                    { 14, "Speed one. That's all you get. However long it takes.", 8, 5, "Stroke at speed 1. You may only cum at speed 1. If you speed up at all, you stop for 30 seconds and start over. This could take a very long time.", "Slow Torture Cum", false, false, false, 8 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PunishmentEnders");

            migrationBuilder.DropColumn(
                name: "AllowSelfOral",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Points",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "HoldSeconds",
                table: "InstructionSteps");
        }
    }
}
