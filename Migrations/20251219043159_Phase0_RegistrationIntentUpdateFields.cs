using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase0_RegistrationIntentUpdateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstitutionAccessCode",
                table: "RegistrationIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "RegistrationIntents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstitutionAccessCode",
                table: "Institutions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InstitutionType",
                table: "Institutions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresUserApproval",
                table: "Institutions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InstitutionAccessCode",
                table: "RegistrationIntents");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "RegistrationIntents");

            migrationBuilder.DropColumn(
                name: "InstitutionAccessCode",
                table: "Institutions");

            migrationBuilder.DropColumn(
                name: "InstitutionType",
                table: "Institutions");

            migrationBuilder.DropColumn(
                name: "RequiresUserApproval",
                table: "Institutions");
        }
    }
}
