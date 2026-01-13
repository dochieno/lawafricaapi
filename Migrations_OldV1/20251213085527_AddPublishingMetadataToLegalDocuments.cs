using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishingMetadataToLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "LegalDocuments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Edition",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ISBN",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublicationYear",
                table: "LegalDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                table: "LegalDocuments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Author",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "Edition",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "ISBN",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "PublicationYear",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "Publisher",
                table: "LegalDocuments");
        }
    }
}
