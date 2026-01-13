using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Allowed = table.Column<bool>(type: "boolean", nullable: false),
                    DecisionReason = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Surface = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AtUtc",
                table: "UsageEvents",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_AtUtc_Allowed",
                table: "UsageEvents",
                columns: new[] { "AtUtc", "Allowed" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_InstitutionId_AtUtc",
                table: "UsageEvents",
                columns: new[] { "InstitutionId", "AtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_LegalDocumentId_AtUtc",
                table: "UsageEvents",
                columns: new[] { "LegalDocumentId", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageEvents");
        }
    }
}
