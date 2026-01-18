using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class updateLawReportCitUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LawReports_Citation",
                table: "LawReports");

            migrationBuilder.DropIndex(
                name: "IX_LawReports_ReportNumber_Year_CaseNumber",
                table: "LawReports");

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_Citation",
                table: "LawReports",
                column: "Citation",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_ReportNumber_Year_CaseNumber",
                table: "LawReports",
                columns: new[] { "ReportNumber", "Year", "CaseNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LawReports_Citation",
                table: "LawReports");

            migrationBuilder.DropIndex(
                name: "IX_LawReports_ReportNumber_Year_CaseNumber",
                table: "LawReports");

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_Citation",
                table: "LawReports",
                column: "Citation");

            migrationBuilder.CreateIndex(
                name: "IX_LawReports_ReportNumber_Year_CaseNumber",
                table: "LawReports",
                columns: new[] { "ReportNumber", "Year", "CaseNumber" },
                unique: true);
        }
    }
}
