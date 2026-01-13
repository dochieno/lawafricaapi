using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorSetupTokenToUserV0 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TwoFactorSetupToken",
                table: "Users",
                newName: "TwoFactorSetupTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TwoFactorSetupTokenHash",
                table: "Users",
                newName: "TwoFactorSetupToken");
        }
    }
}
