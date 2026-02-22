using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLawyerServicesAndRateCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LawyerServices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Slug = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerServices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LawyerServiceOfferings",
                columns: table => new
                {
                    LawyerProfileId = table.Column<int>(type: "integer", nullable: false),
                    LawyerServiceId = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    MinFee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    MaxFee = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Unit = table.Column<short>(type: "smallint", nullable: false),
                    Notes = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerServiceOfferings", x => new { x.LawyerProfileId, x.LawyerServiceId });
                    table.ForeignKey(
                        name: "FK_LawyerServiceOfferings_LawyerProfiles_LawyerProfileId",
                        column: x => x.LawyerProfileId,
                        principalTable: "LawyerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LawyerServiceOfferings_LawyerServices_LawyerServiceId",
                        column: x => x.LawyerServiceId,
                        principalTable: "LawyerServices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LawyerServiceOfferings_LawyerServiceId",
                table: "LawyerServiceOfferings",
                column: "LawyerServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerServices_IsActive_SortOrder",
                table: "LawyerServices",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LawyerServices_Name",
                table: "LawyerServices",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawyerServiceOfferings");

            migrationBuilder.DropTable(
                name: "LawyerServices");
        }
    }
}
