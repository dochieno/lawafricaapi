using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class MakeReferenceNumberNullable : Migration
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

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

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

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
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
        }
    }
}
