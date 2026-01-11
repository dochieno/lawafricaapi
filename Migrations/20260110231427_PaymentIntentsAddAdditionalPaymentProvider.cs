using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class PaymentIntentsAddAdditionalPaymentProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderChannel",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProviderPaidAt",
                table: "PaymentIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRawJson",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderReference",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTransactionId",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_ProviderReference",
                table: "PaymentIntents",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_Provider_ProviderTransactionId",
                table: "PaymentIntents",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true,
                filter: "\"ProviderTransactionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_Provider_ProviderReference",
                table: "PaymentIntents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_Provider_ProviderTransactionId",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProviderChannel",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProviderPaidAt",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProviderRawJson",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProviderReference",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ProviderTransactionId",
                table: "PaymentIntents");
        }
    }
}
