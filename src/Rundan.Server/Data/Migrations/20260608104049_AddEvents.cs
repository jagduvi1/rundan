using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EventId",
                table: "Activities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Order",
                table: "Activities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    JoinCode = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_EventId_Order",
                table: "Activities",
                columns: new[] { "EventId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_JoinCode",
                table: "Events",
                column: "JoinCode",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_Events_EventId",
                table: "Activities",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Activities_Events_EventId",
                table: "Activities");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Activities_EventId_Order",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "EventId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Order",
                table: "Activities");
        }
    }
}
