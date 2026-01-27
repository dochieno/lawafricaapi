using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLawReportContentBlocksV0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Version",
                table: "LawReportContentJsonCaches");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "LawReportContentJsonCaches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "LawReportContentJsonCaches");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "LawReportContentJsonCaches",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
