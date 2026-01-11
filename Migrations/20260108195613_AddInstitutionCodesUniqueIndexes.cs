using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionCodesUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Institutions_InstitutionAccessCode",
                table: "Institutions",
                column: "InstitutionAccessCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Institutions_RegistrationNumber",
                table: "Institutions",
                column: "RegistrationNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Institutions_InstitutionAccessCode",
                table: "Institutions");

            migrationBuilder.DropIndex(
                name: "IX_Institutions_RegistrationNumber",
                table: "Institutions");
        }
    }
}
