using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddContentProductLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentProductLegalDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentProductLegalDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentProductLegalDocuments_ContentProducts_ContentProduct~",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentProductLegalDocuments_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductLegalDocuments_ContentProductId_LegalDocument~",
                table: "ContentProductLegalDocuments",
                columns: new[] { "ContentProductId", "LegalDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductLegalDocuments_LegalDocumentId",
                table: "ContentProductLegalDocuments",
                column: "LegalDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentProductLegalDocuments");
        }
    }
}
