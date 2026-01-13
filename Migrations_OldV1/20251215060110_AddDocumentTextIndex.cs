using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentTextIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TableOfContentsJson",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentTextIndexes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    IndexedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTextIndexes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentTextIndexes_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTextIndexes_LegalDocumentId_PageNumber",
                table: "DocumentTextIndexes",
                columns: new[] { "LegalDocumentId", "PageNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentTextIndexes");

            migrationBuilder.DropColumn(
                name: "TableOfContentsJson",
                table: "LegalDocuments");
        }
    }
}
