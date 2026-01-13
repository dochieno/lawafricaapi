using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionSubscriptionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_EndDate",
                table: "InstitutionProductSubscriptions",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_StartDate",
                table: "InstitutionProductSubscriptions",
                column: "StartDate");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_Status_EndDate",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "Status", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_Status_StartDate",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_EndDate",
                table: "InstitutionProductSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_StartDate",
                table: "InstitutionProductSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_Status_EndDate",
                table: "InstitutionProductSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_Status_StartDate",
                table: "InstitutionProductSubscriptions");
        }
    }
}
