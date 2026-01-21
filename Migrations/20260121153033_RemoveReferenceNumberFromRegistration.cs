using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReferenceNumberFromRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_InstitutionId_ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionMemberships_InstitutionId_ReferenceNumber",
                table: "InstitutionMemberships");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_InstitutionId",
                table: "RegistrationIntents",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_InstitutionId",
                table: "InstitutionMemberships",
                column: "InstitutionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_InstitutionId",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionMemberships_InstitutionId",
                table: "InstitutionMemberships");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_InstitutionId_ReferenceNumber",
                table: "RegistrationIntents",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true,
                filter: "\"InstitutionId\" IS NOT NULL AND \"ReferenceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_ReferenceNumber",
                table: "RegistrationIntents",
                column: "ReferenceNumber",
                unique: true,
                filter: "\"InstitutionId\" IS NULL AND \"ReferenceNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_InstitutionId_ReferenceNumber",
                table: "InstitutionMemberships",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true);
        }
    }
}
