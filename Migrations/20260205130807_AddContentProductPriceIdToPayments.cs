using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddContentProductPriceIdToPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentProductPriceId",
                table: "PaymentIntents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ContentProductPriceId",
                table: "InvoiceLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_ContentProductPriceId",
                table: "PaymentIntents",
                column: "ContentProductPriceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_ContentProductPriceId",
                table: "InvoiceLines",
                column: "ContentProductPriceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_ContentProductPriceId",
                table: "PaymentIntents");

            migrationBuilder.DropIndex(
                name: "IX_InvoiceLines_ContentProductPriceId",
                table: "InvoiceLines");

            migrationBuilder.DropColumn(
                name: "ContentProductPriceId",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ContentProductPriceId",
                table: "InvoiceLines");
        }
    }
}
