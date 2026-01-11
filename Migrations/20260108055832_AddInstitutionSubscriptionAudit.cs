using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInstitutionSubscriptionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId",
                table: "InstitutionProductSubscriptions");

            migrationBuilder.CreateTable(
                name: "InstitutionSubscriptionAudits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SubscriptionId = table.Column<int>(type: "integer", nullable: false),
                    Action = table.Column<int>(type: "integer", nullable: false),
                    PerformedByUserId = table.Column<int>(type: "integer", nullable: true),
                    OldStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OldStatus = table.Column<int>(type: "integer", nullable: false),
                    NewStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NewStatus = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionSubscriptionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionSubscriptionAudits_InstitutionProductSubscriptio~",
                        column: x => x.SubscriptionId,
                        principalTable: "InstitutionProductSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId_ContentProduc~",
                table: "InstitutionProductSubscriptions",
                columns: new[] { "InstitutionId", "ContentProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionSubscriptionAudits_SubscriptionId_CreatedAt",
                table: "InstitutionSubscriptionAudits",
                columns: new[] { "SubscriptionId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InstitutionSubscriptionAudits");

            migrationBuilder.DropIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId_ContentProduc~",
                table: "InstitutionProductSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId",
                table: "InstitutionProductSubscriptions",
                column: "InstitutionId");
        }
    }
}
