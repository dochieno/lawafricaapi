using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase241_AddCheckoutRequestIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_CheckoutRequestId",
                table: "PaymentIntents",
                column: "CheckoutRequestId",
                unique: true,
                filter: "\"CheckoutRequestId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_CheckoutRequestId",
                table: "PaymentIntents");
        }
    }
}
