using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class PerPlayerScoringAndActivityGeofence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "ScoreEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Activities",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Activities",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RadiusMeters",
                table: "Activities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ScoreEntryMode",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ScoreEntries_UserId",
                table: "ScoreEntries",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScoreEntries_Users_UserId",
                table: "ScoreEntries",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScoreEntries_Users_UserId",
                table: "ScoreEntries");

            migrationBuilder.DropIndex(
                name: "IX_ScoreEntries_UserId",
                table: "ScoreEntries");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ScoreEntries");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "RadiusMeters",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "ScoreEntryMode",
                table: "Activities");
        }
    }
}
