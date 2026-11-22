using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mangrove.Cms.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicUids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Users",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Terms",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Posts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Media",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Uid",
                table: "Comments",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Give every existing row its own random Uid (same uppercase text form EF Core writes for Guid).
            foreach (var table in new[] { "Users", "Terms", "Posts", "Notices", "Media", "Comments" })
            {
                migrationBuilder.Sql($@"UPDATE ""{table}"" SET ""Uid"" = upper(
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' ||
                    substr('89AB', 1 + (abs(random()) % 4), 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)));");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Users_Uid",
                table: "Users",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Terms_Uid",
                table: "Terms",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Uid",
                table: "Posts",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Uid",
                table: "Notices",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Media_Uid",
                table: "Media",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Comments_Uid",
                table: "Comments",
                column: "Uid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Uid",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Terms_Uid",
                table: "Terms");

            migrationBuilder.DropIndex(
                name: "IX_Posts_Uid",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_Notices_Uid",
                table: "Notices");

            migrationBuilder.DropIndex(
                name: "IX_Media_Uid",
                table: "Media");

            migrationBuilder.DropIndex(
                name: "IX_Comments_Uid",
                table: "Comments");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Terms");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Media");

            migrationBuilder.DropColumn(
                name: "Uid",
                table: "Comments");
        }
    }
}
