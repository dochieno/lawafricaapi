using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiLegalDocumentSectionSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiLegalDocumentSectionSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    TocEntryId = table.Column<int>(type: "integer", nullable: false),
                    StartPage = table.Column<int>(type: "integer", nullable: false),
                    EndPage = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    TokensIn = table.Column<int>(type: "integer", nullable: true),
                    TokensOut = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiLegalDocumentSectionSummaries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiLegalDocumentSectionSummaries_UserId_LegalDocumentId_TocE~",
                table: "AiLegalDocumentSectionSummaries",
                columns: new[] { "UserId", "LegalDocumentId", "TocEntryId", "StartPage", "EndPage", "Type", "PromptVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiLegalDocumentSectionSummaries");
        }
    }
}
