using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentTocEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LegalDocumentTocEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    ParentId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    TargetType = table.Column<int>(type: "integer", nullable: false),
                    StartPage = table.Column<int>(type: "integer", nullable: true),
                    EndPage = table.Column<int>(type: "integer", nullable: true),
                    AnchorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PageLabel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentTocEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentTocEntries_LegalDocumentTocEntries_ParentId",
                        column: x => x.ParentId,
                        principalTable: "LegalDocumentTocEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LegalDocumentTocEntries_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentTocEntries_LegalDocumentId_AnchorId",
                table: "LegalDocumentTocEntries",
                columns: new[] { "LegalDocumentId", "AnchorId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentTocEntries_LegalDocumentId_ParentId_Order",
                table: "LegalDocumentTocEntries",
                columns: new[] { "LegalDocumentId", "ParentId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentTocEntries_ParentId",
                table: "LegalDocumentTocEntries",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LegalDocumentTocEntries");
        }
    }
}
