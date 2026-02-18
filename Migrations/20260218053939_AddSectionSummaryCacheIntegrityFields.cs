using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddSectionSummaryCacheIntegrityFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CacheKey",
                table: "AiLegalDocumentSectionSummaries",
                type: "character varying(240)",
                maxLength: 240,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "AiLegalDocumentSectionSummaries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelUsed",
                table: "AiLegalDocumentSectionSummaries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerKey",
                table: "AiLegalDocumentSectionSummaries",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SectionTitle",
                table: "AiLegalDocumentSectionSummaries",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheKey",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.DropColumn(
                name: "ModelUsed",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.DropColumn(
                name: "OwnerKey",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.DropColumn(
                name: "SectionTitle",
                table: "AiLegalDocumentSectionSummaries");
        }
    }
}
