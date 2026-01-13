using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionSubscriptionActionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InstitutionSubscriptionActionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    RequestType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<int>(type: "integer", nullable: false),
                    ReviewedByUserId = table.Column<int>(type: "integer", nullable: true),
                    RequestNotes = table.Column<string>(type: "text", nullable: true),
                    ReviewNotes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionSubscriptionActionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionSubscriptionActionRequests_InstitutionProductSub~",
                        column: x => x.SubscriptionId,
                        principalTable: "InstitutionProductSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionActionRequests_Status_CreatedAt",
                table: "InstitutionSubscriptionActionRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionActionRequests_SubscriptionId_Status",
                table: "InstitutionSubscriptionActionRequests",
                columns: new[] { "SubscriptionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstitutionSubscriptionActionRequests");
        }
    }
}
