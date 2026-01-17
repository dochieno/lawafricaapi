using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLawReportsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "LegalDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "LawReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: false),
                    Citation = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReportNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    CaseNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    DecisionType = table.Column<int>(type: "integer", nullable: false),
                    CaseType = table.Column<int>(type: "integer", nullable: false),
                    Court = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Parties = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Judges = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DecisionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ContentText = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawReports_LegalDocuments_LegalDocumentId",
                        column: x => x.LegalDocumentId,
                        principalTable: "LegalDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_Citation",
                table: "LawReports",
                column: "Citation");

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_LegalDocumentId",
                table: "LawReports",
                column: "LegalDocumentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_ReportNumber_Year_CaseNumber",
                table: "LawReports",
                columns: new[] { "ReportNumber", "Year", "CaseNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawReports");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "LegalDocuments");
        }
    }
}
