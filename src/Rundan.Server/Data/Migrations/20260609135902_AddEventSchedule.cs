using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAt",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAt",
                table: "Events",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "StartsAt",
                table: "Events");
        }
    }
}
