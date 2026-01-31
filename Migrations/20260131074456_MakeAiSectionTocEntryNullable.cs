using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class MakeAiSectionTocEntryNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "TocEntryId",
                table: "AiLegalDocumentSectionSummaries",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.CreateTable(
                name: "AiDailyAiUsage",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    DayUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Requests = table.Column<int>(type: "integer", nullable: false),
                    Feature = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiDailyAiUsage", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiDailyAiUsage_UserId_DayUtc_Feature",
                table: "AiDailyAiUsage",
                columns: new[] { "UserId", "DayUtc", "Feature" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiDailyAiUsage");

            migrationBuilder.AlterColumn<int>(
                name: "TocEntryId",
                table: "AiLegalDocumentSectionSummaries",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
