using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentPublicPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Currency",
                table: "LegalDocuments",
                newName: "PublicCurrency");

            migrationBuilder.AddColumn<bool>(
                name: "AllowPublicPurchase",
                table: "LegalDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowPublicPurchase",
                table: "LegalDocuments");

            migrationBuilder.RenameColumn(
                name: "PublicCurrency",
                table: "LegalDocuments",
                newName: "Currency");
        }
    }
}
