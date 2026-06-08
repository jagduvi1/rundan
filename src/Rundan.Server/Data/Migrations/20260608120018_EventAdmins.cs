using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class EventAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "EventMembers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "EventMembers");
        }
    }
}
