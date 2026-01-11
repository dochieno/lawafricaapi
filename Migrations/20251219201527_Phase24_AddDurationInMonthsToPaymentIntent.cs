using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase24_AddDurationInMonthsToPaymentIntent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminNotes",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "PaymentIntents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedByUserId",
                table: "PaymentIntents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationInMonths",
                table: "PaymentIntents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManualReference",
                table: "PaymentIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "PaymentIntents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentIntents_ApprovedByUserId",
                table: "PaymentIntents",
                column: "ApprovedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentIntents_Users_ApprovedByUserId",
                table: "PaymentIntents",
                column: "ApprovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentIntents_Users_ApprovedByUserId",
                table: "PaymentIntents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentIntents_ApprovedByUserId",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "AdminNotes",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "DurationInMonths",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "ManualReference",
                table: "PaymentIntents");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "PaymentIntents");
        }
    }
}
