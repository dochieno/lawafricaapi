using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAudienceAccessToContentProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludedInInstitutionBundle",
                table: "ContentProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludedInPublicBundle",
                table: "ContentProducts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "InstitutionAccessModel",
                table: "ContentProducts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PublicAccessModel",
                table: "ContentProducts",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IncludedInInstitutionBundle",
                table: "ContentProducts");

            migrationBuilder.DropColumn(
                name: "IncludedInPublicBundle",
                table: "ContentProducts");

            migrationBuilder.DropColumn(
                name: "InstitutionAccessModel",
                table: "ContentProducts");

            migrationBuilder.DropColumn(
                name: "PublicAccessModel",
                table: "ContentProducts");
        }
    }
}
