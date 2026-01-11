using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentPricingAndPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PublicPrice",
                table: "LegalDocuments",
                type: "numeric",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserLegalDocumentPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    PaymentReference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLegalDocumentPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLegalDocumentPurchases_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserLegalDocumentPurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalDocumentPurchases_LegalDocumentId",
                table: "UserLegalDocumentPurchases",
                column: "LegalDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLegalDocumentPurchases_UserId_LegalDocumentId",
                table: "UserLegalDocumentPurchases",
                columns: new[] { "UserId", "LegalDocumentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserLegalDocumentPurchases");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "PublicPrice",
                table: "LegalDocuments");
        }
    }
}
