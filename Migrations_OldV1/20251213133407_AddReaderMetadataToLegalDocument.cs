using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddReaderMetadataToLegalDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverImagePath",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileHashSha256",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastIndexedAt",
                table: "LegalDocuments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverImagePath",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "FileHashSha256",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "LastIndexedAt",
                table: "LegalDocuments");
        }
    }
}
