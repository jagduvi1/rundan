using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rundan.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class CourtsAndOrderOfPlay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CourtId",
                table: "BracketMatches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CourtLabel",
                table: "Activities",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Courts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ActivityId = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courts_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BracketMatches_CourtId",
                table: "BracketMatches",
                column: "CourtId");

            migrationBuilder.CreateIndex(
                name: "IX_Courts_ActivityId_Order",
                table: "Courts",
                columns: new[] { "ActivityId", "Order" });

            migrationBuilder.AddForeignKey(
                name: "FK_BracketMatches_Courts_CourtId",
                table: "BracketMatches",
                column: "CourtId",
                principalTable: "Courts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BracketMatches_Courts_CourtId",
                table: "BracketMatches");

            migrationBuilder.DropTable(
                name: "Courts");

            migrationBuilder.DropIndex(
                name: "IX_BracketMatches_CourtId",
                table: "BracketMatches");

            migrationBuilder.DropColumn(
                name: "CourtId",
                table: "BracketMatches");

            migrationBuilder.DropColumn(
                name: "CourtLabel",
                table: "Activities");
        }
    }
}
