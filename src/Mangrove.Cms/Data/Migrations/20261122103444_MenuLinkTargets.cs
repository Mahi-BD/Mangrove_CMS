using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mangrove.Cms.Data.Migrations
{
    /// <inheritdoc />
    public partial class MenuLinkTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LinkId",
                table: "MenuItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkType",
                table: "MenuItems",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "custom");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinkId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "LinkType",
                table: "MenuItems");
        }
    }
}
