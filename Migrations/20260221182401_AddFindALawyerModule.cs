using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddFindALawyerModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LawyerProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    FirmName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Bio = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    PrimaryPhone = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    PublicEmail = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    HighestCourtAllowedId = table.Column<int>(type: "integer", nullable: true),
                    PrimaryTownId = table.Column<int>(type: "integer", nullable: true),
                    GooglePlaceId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    GoogleFormattedAddress = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: true),
                    Longitude = table.Column<double>(type: "double precision", nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawyerProfiles_Courts_HighestCourtAllowedId",
                        column: x => x.HighestCourtAllowedId,
                        principalTable: "Courts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LawyerProfiles_Towns_PrimaryTownId",
                        column: x => x.PrimaryTownId,
                        principalTable: "Towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LawyerProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PracticeAreas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LawyerTowns",
                columns: table => new
                {
                    LawyerProfileId = table.Column<int>(type: "integer", nullable: false),
                    TownId = table.Column<int>(type: "integer", nullable: false),
                    IsOfficeLocation = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerTowns", x => new { x.LawyerProfileId, x.TownId });
                    table.ForeignKey(
                        name: "FK_LawyerTowns_LawyerProfiles_LawyerProfileId",
                        column: x => x.LawyerProfileId,
                        principalTable: "LawyerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LawyerTowns_Towns_TownId",
                        column: x => x.TownId,
                        principalTable: "Towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LawyerInquiries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LawyerProfileId = table.Column<int>(type: "integer", nullable: true),
                    RequesterUserId = table.Column<int>(type: "integer", nullable: false),
                    PracticeAreaId = table.Column<int>(type: "integer", nullable: true),
                    TownId = table.Column<int>(type: "integer", nullable: true),
                    ProblemSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    PreferredContactMethod = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerInquiries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LawyerInquiries_LawyerProfiles_LawyerProfileId",
                        column: x => x.LawyerProfileId,
                        principalTable: "LawyerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LawyerInquiries_PracticeAreas_PracticeAreaId",
                        column: x => x.PracticeAreaId,
                        principalTable: "PracticeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LawyerInquiries_Towns_TownId",
                        column: x => x.TownId,
                        principalTable: "Towns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LawyerInquiries_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LawyerPracticeAreas",
                columns: table => new
                {
                    LawyerProfileId = table.Column<int>(type: "integer", nullable: false),
                    PracticeAreaId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LawyerPracticeAreas", x => new { x.LawyerProfileId, x.PracticeAreaId });
                    table.ForeignKey(
                        name: "FK_LawyerPracticeAreas_LawyerProfiles_LawyerProfileId",
                        column: x => x.LawyerProfileId,
                        principalTable: "LawyerProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LawyerPracticeAreas_PracticeAreas_PracticeAreaId",
                        column: x => x.PracticeAreaId,
                        principalTable: "PracticeAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LawyerInquiries_LawyerProfileId",
                table: "LawyerInquiries",
                column: "LawyerProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerInquiries_PracticeAreaId",
                table: "LawyerInquiries",
                column: "PracticeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerInquiries_RequesterUserId",
                table: "LawyerInquiries",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerInquiries_TownId",
                table: "LawyerInquiries",
                column: "TownId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerPracticeAreas_PracticeAreaId",
                table: "LawyerPracticeAreas",
                column: "PracticeAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerProfiles_HighestCourtAllowedId",
                table: "LawyerProfiles",
                column: "HighestCourtAllowedId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerProfiles_PrimaryTownId",
                table: "LawyerProfiles",
                column: "PrimaryTownId");

            migrationBuilder.CreateIndex(
                name: "IX_LawyerProfiles_UserId",
                table: "LawyerProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LawyerTowns_TownId",
                table: "LawyerTowns",
                column: "TownId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LawyerInquiries");

            migrationBuilder.DropTable(
                name: "LawyerPracticeAreas");

            migrationBuilder.DropTable(
                name: "LawyerTowns");

            migrationBuilder.DropTable(
                name: "PracticeAreas");

            migrationBuilder.DropTable(
                name: "LawyerProfiles");
        }
    }
}
