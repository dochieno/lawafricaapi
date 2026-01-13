using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_ContentProductsAndEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContentProductId",
                table: "LegalDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    AccessModel = table.Column<int>(type: "integer", nullable: false),
                    AvailableToInstitutions = table.Column<bool>(type: "boolean", nullable: false),
                    AvailableToPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstitutionProductSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstitutionId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstitutionProductSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstitutionProductSubscriptions_ContentProducts_ContentProd~",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InstitutionProductSubscriptions_Institutions_InstitutionId",
                        column: x => x.InstitutionId,
                        principalTable: "Institutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProductOwnerships",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TransactionReference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProductOwnerships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProductOwnerships_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProductOwnerships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserProductSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ContentProductId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProductSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserProductSubscriptions_ContentProducts_ContentProductId",
                        column: x => x.ContentProductId,
                        principalTable: "ContentProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserProductSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_ContentProductId",
                table: "LegalDocuments",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_ContentProductId",
                table: "InstitutionProductSubscriptions",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_InstitutionProductSubscriptions_InstitutionId",
                table: "InstitutionProductSubscriptions",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductOwnerships_ContentProductId",
                table: "UserProductOwnerships",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductOwnerships_UserId",
                table: "UserProductOwnerships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_ContentProductId",
                table: "UserProductSubscriptions",
                column: "ContentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_UserProductSubscriptions_UserId",
                table: "UserProductSubscriptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LegalDocuments_ContentProducts_ContentProductId",
                table: "LegalDocuments",
                column: "ContentProductId",
                principalTable: "ContentProducts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LegalDocuments_ContentProducts_ContentProductId",
                table: "LegalDocuments");

            migrationBuilder.DropTable(
                name: "InstitutionProductSubscriptions");

            migrationBuilder.DropTable(
                name: "UserProductOwnerships");

            migrationBuilder.DropTable(
                name: "UserProductSubscriptions");

            migrationBuilder.DropTable(
                name: "ContentProducts");

            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_ContentProductId",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "ContentProductId",
                table: "LegalDocuments");
        }
    }
}
