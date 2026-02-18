using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AiSectionSummaryCacheKeysV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiLegalDocumentSectionSummaries_UserId_LegalDocumentId_TocE~",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "AiLegalDocumentSectionSummaries",
                newName: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiLegalDocumentSectionSummaries_OwnerKey_LegalDocumentId_To~",
                table: "AiLegalDocumentSectionSummaries",
                columns: new[] { "OwnerKey", "LegalDocumentId", "TocEntryId", "StartPage", "EndPage", "Type", "PromptVersion", "ContentHash" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AiLegalDocumentSectionSummaries_OwnerKey_LegalDocumentId_To~",
                table: "AiLegalDocumentSectionSummaries");

            migrationBuilder.RenameColumn(
                name: "CreatedByUserId",
                table: "AiLegalDocumentSectionSummaries",
                newName: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiLegalDocumentSectionSummaries_UserId_LegalDocumentId_TocE~",
                table: "AiLegalDocumentSectionSummaries",
                columns: new[] { "UserId", "LegalDocumentId", "TocEntryId", "StartPage", "EndPage", "Type", "PromptVersion" },
                unique: true);
        }
    }
}
