using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueUsernameAndReferenceNumber_v2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_InstitutionId",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionMemberships_InstitutionId",
                table: "InstitutionMemberships");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_InstitutionId_ReferenceNumber",
                table: "RegistrationIntents",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true,
                filter: "\"InstitutionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_ReferenceNumber",
                table: "RegistrationIntents",
                column: "ReferenceNumber",
                unique: true,
                filter: "\"InstitutionId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_Username",
                table: "RegistrationIntents",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_InstitutionId_ReferenceNumber",
                table: "InstitutionMemberships",
                columns: new[] { "InstitutionId", "ReferenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_InstitutionId_ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_RegistrationIntents_Username",
                table: "RegistrationIntents");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionMemberships_InstitutionId_ReferenceNumber",
                table: "InstitutionMemberships");

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationIntents_InstitutionId",
                table: "RegistrationIntents",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionMemberships_InstitutionId",
                table: "InstitutionMemberships",
                column: "InstitutionId");
        }
    }
}
