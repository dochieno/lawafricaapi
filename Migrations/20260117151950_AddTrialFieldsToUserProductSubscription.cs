using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddTrialFieldsToUserProductSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProductSubscriptions_UserId",
                table: "UserProductSubscriptions");

            migrationBuilder.AddColumn<int>(
                name: "GrantedByUserId",
                table: "UserProductSubscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTrial",
                table: "UserProductSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_GrantedByUserId",
                table: "UserProductSubscriptions",
                column: "GrantedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_UserId_ContentProductId",
                table: "UserProductSubscriptions",
                columns: new[] { "UserId", "ContentProductId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UserProductSubscriptions_Users_GrantedByUserId",
                table: "UserProductSubscriptions",
                column: "GrantedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProductSubscriptions_Users_GrantedByUserId",
                table: "UserProductSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_UserProductSubscriptions_GrantedByUserId",
                table: "UserProductSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_UserProductSubscriptions_UserId_ContentProductId",
                table: "UserProductSubscriptions");

            migrationBuilder.DropColumn(
                name: "GrantedByUserId",
                table: "UserProductSubscriptions");

            migrationBuilder.DropColumn(
                name: "IsTrial",
                table: "UserProductSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_UserId",
                table: "UserProductSubscriptions",
                column: "UserId");
        }
    }
}
