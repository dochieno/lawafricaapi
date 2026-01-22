using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddVatRatesAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "VatRateId",
                table: "LegalDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "VatRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RatePercent = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    CountryScope = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VatRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VatRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Purpose = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CountryCode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    VatRateId = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VatRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VatRules_VatRates_VatRateId",
                        column: x => x.VatRateId,
                        principalTable: "VatRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_VatRateId",
                table: "LegalDocuments",
                column: "VatRateId");

            migrationBuilder.CreateIndex(
                name: "IX_VatRates_Code",
                table: "VatRates",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VatRules_Purpose_CountryCode_Priority",
                table: "VatRules",
                columns: new[] { "Purpose", "CountryCode", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_VatRules_VatRateId",
                table: "VatRules",
                column: "VatRateId");

            migrationBuilder.AddForeignKey(
                name: "FK_LegalDocuments_VatRates_VatRateId",
                table: "LegalDocuments",
                column: "VatRateId",
                principalTable: "VatRates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LegalDocuments_VatRates_VatRateId",
                table: "LegalDocuments");

            migrationBuilder.DropTable(
                name: "VatRules");

            migrationBuilder.DropTable(
                name: "VatRates");

            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_VatRateId",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "VatRateId",
                table: "LegalDocuments");
        }
    }
}
