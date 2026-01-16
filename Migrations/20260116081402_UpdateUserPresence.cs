using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSeenIp",
                table: "UserPresences",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSeenUserAgent",
                table: "UserPresences",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSeenIp",
                table: "UserPresences");

            migrationBuilder.DropColumn(
                name: "LastSeenUserAgent",
                table: "UserPresences");
        }
    }
}
