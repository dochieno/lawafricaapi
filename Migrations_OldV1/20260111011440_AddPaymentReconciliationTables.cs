using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentReconciliationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: true),
                    FromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationRuns_Users_PerformedByUserId",
                        column: x => x.PerformedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentReconciliationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    PaymentIntentId = table.Column<int>(type: "integer", nullable: true),
                    ProviderTransactionIdRef = table.Column<long>(type: "bigint", nullable: true),
                    ProviderTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    InvoiceId = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentReconciliationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentIntents_PaymentIntentId",
                        column: x => x.PaymentIntentId,
                        principalTable: "PaymentIntents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentProviderTransactions_Prov~",
                        column: x => x.ProviderTransactionId,
                        principalTable: "PaymentProviderTransactions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PaymentReconciliationItems_PaymentReconciliationRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PaymentReconciliationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_InvoiceId",
                table: "PaymentReconciliationItems",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_PaymentIntentId",
                table: "PaymentReconciliationItems",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Provider_Reference",
                table: "PaymentReconciliationItems",
                columns: new[] { "Provider", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_ProviderTransactionId",
                table: "PaymentReconciliationItems",
                column: "ProviderTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Reason",
                table: "PaymentReconciliationItems",
                column: "Reason");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_RunId",
                table: "PaymentReconciliationItems",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationItems_Status",
                table: "PaymentReconciliationItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationRuns_CreatedAt",
                table: "PaymentReconciliationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentReconciliationRuns_PerformedByUserId",
                table: "PaymentReconciliationRuns",
                column: "PerformedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentReconciliationItems");

            migrationBuilder.DropTable(
                name: "PaymentReconciliationRuns");
        }
    }
}
