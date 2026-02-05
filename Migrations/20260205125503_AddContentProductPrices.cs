using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddContentProductPrices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentProductPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    Audience = table.Column<short>(type: "smallint", nullable: false),
                    BillingPeriod = table.Column<short>(type: "smallint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EffectiveToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentProductPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentProductPrices_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductPrices_ContentProductId",
                table: "ContentProductPrices",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductPrices_ContentProductId_Audience_BillingPerio~",
                table: "ContentProductPrices",
                columns: new[] { "ContentProductId", "Audience", "BillingPeriod", "Currency" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentProductPrices_IsActive_EffectiveFromUtc_EffectiveToU~",
                table: "ContentProductPrices",
                columns: new[] { "IsActive", "EffectiveFromUtc", "EffectiveToUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentProductPrices");
        }
    }
}
