using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddHighlightFieldsToLegalDocumentNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CharOffsetEnd",
                table: "LegalDocumentNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CharOffsetStart",
                table: "LegalDocumentNotes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HighlightedText",
                table: "LegalDocumentNotes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CharOffsetEnd",
                table: "LegalDocumentNotes");

            migrationBuilder.DropColumn(
                name: "CharOffsetStart",
                table: "LegalDocumentNotes");

            migrationBuilder.DropColumn(
                name: "HighlightedText",
                table: "LegalDocumentNotes");
        }
    }
}
