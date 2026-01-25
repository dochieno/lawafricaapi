using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiLawReportSummaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiLawReportSummaries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LawReportId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SummaryType = table.Column<int>(type: "integer", nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: false),
                    InputChars = table.Column<int>(type: "integer", nullable: false),
                    OutputChars = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiLawReportSummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AiUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PeriodKey = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SummariesGenerated = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiLawReportSummaries_LawReportId_UserId_SummaryType",
                table: "AiLawReportSummaries",
                columns: new[] { "LawReportId", "UserId", "SummaryType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiUsages_UserId_PeriodKey",
                table: "AiUsages",
                columns: new[] { "UserId", "PeriodKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiLawReportSummaries");

            migrationBuilder.DropTable(
                name: "AiUsages");
        }
    }
}
