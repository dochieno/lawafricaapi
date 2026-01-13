using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAuditTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentProviderTransactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PaymentProviderWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderEventId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    DedupeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    SignatureValid = table.Column<bool>(type: "boolean", nullable: true),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ProcessingError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RawBody = table.Column<string>(type: "text", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentProviderWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_PaidAt",
                table: "PaymentProviderTransactions",
                column: "PaidAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_Provider_ProviderTransactionId",
                table: "PaymentProviderTransactions",
                columns: new[] { "Provider", "ProviderTransactionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderTransactions_Provider_Reference",
                table: "PaymentProviderTransactions",
                columns: new[] { "Provider", "Reference" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_Provider_DedupeHash",
                table: "PaymentProviderWebhookEvents",
                columns: new[] { "Provider", "DedupeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_ReceivedAt",
                table: "PaymentProviderWebhookEvents",
                column: "ReceivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentProviderWebhookEvents_Reference",
                table: "PaymentProviderWebhookEvents",
                column: "Reference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentProviderTransactions");

            migrationBuilder.DropTable(
                name: "PaymentProviderWebhookEvents");
        }
    }
}
