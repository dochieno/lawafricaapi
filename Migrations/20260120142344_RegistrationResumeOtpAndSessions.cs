using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class RegistrationResumeOtpAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RegistrationResumeOtps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailNormalized = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationResumeOtps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegistrationResumeSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EmailNormalized = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegistrationResumeSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationResumeOtps_EmailNormalized_ExpiresAtUtc",
                table: "RegistrationResumeOtps",
                columns: new[] { "EmailNormalized", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationResumeOtps_EmailNormalized_IsUsed",
                table: "RegistrationResumeOtps",
                columns: new[] { "EmailNormalized", "IsUsed" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationResumeSessions_EmailNormalized_ExpiresAtUtc",
                table: "RegistrationResumeSessions",
                columns: new[] { "EmailNormalized", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RegistrationResumeSessions_TokenHash",
                table: "RegistrationResumeSessions",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RegistrationResumeOtps");

            migrationBuilder.DropTable(
                name: "RegistrationResumeSessions");
        }
    }
}
