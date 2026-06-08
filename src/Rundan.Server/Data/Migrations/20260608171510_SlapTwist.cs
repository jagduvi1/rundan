using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SlapTwist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SlapMode",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Slaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: false),
                    SlapperUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SlappedUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RecipientUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Penalty = table.Column<int>(type: "INTEGER", nullable: false),
                    Skipped = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Slaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Slaps_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Slaps_ActivityId",
                table: "Slaps",
                column: "ActivityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Slaps_EventId",
                table: "Slaps",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Slaps");

            migrationBuilder.DropColumn(
                name: "SlapMode",
                table: "Events");
        }
    }
}
