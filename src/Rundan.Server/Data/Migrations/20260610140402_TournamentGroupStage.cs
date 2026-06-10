using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class TournamentGroupStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Seed",
                table: "Participants",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GroupIndex",
                table: "BracketMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Pool",
                table: "BracketMatches",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AdvanceToPlayoffA",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AdvanceToPlayoffB",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupBestOfSets",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupCount",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupGamesToWinSet",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "GroupMatchFormat",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "PlayoffAConsolation",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PlayoffBConsolation",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TournamentScoring",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "UseGroupStage",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "UseManualSeeding",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Seed",
                table: "Participants");

            migrationBuilder.DropColumn(
                name: "GroupIndex",
                table: "BracketMatches");

            migrationBuilder.DropColumn(
                name: "Pool",
                table: "BracketMatches");

            migrationBuilder.DropColumn(
                name: "AdvanceToPlayoffA",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "AdvanceToPlayoffB",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "GroupBestOfSets",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "GroupCount",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "GroupGamesToWinSet",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "GroupMatchFormat",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PlayoffAConsolation",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "PlayoffBConsolation",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "TournamentScoring",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "UseGroupStage",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "UseManualSeeding",
                table: "Activities");
        }
    }
}
