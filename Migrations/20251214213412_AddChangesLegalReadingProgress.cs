using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddChangesLegalReadingProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UpdatedAt",
                table: "LegalDocumentProgress",
                newName: "LastReadAt");

            migrationBuilder.AlterColumn<double>(
                name: "Percentage",
                table: "LegalDocumentProgress",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CharOffset",
                table: "LegalDocumentProgress",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "LegalDocumentProgress",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "LegalDocumentProgress",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalSecondsRead",
                table: "LegalDocumentProgress",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharOffset",
                table: "LegalDocumentProgress");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "LegalDocumentProgress");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "LegalDocumentProgress");

            migrationBuilder.DropColumn(
                name: "TotalSecondsRead",
                table: "LegalDocumentProgress");

            migrationBuilder.RenameColumn(
                name: "LastReadAt",
                table: "LegalDocumentProgress",
                newName: "UpdatedAt");

            migrationBuilder.AlterColumn<double>(
                name: "Percentage",
                table: "LegalDocumentProgress",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");
        }
    }
}
