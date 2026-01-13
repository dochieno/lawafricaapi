using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorSetupTokenToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwoFactorSetupToken",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TwoFactorSetupTokenExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorSetupToken",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TwoFactorSetupTokenExpiry",
                table: "Users");
        }
    }
}
