using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiCommentaryThreads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "LegalDocuments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AiCommentarySettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RetentionMonths = table.Column<int>(type: "integer", nullable: false),
                    EnableUserHistory = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCommentarySettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCommentarySettings_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AiCommentaryThreads",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    InstitutionId = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryId = table.Column<int>(type: "integer", nullable: true),
                    CountryName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CountryIso = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    RegionLabel = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LastModel = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCommentaryThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCommentaryThreads_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiCommentaryMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ThreadId = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DisclaimerVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    InputChars = table.Column<int>(type: "integer", nullable: true),
                    OutputChars = table.Column<int>(type: "integer", nullable: true),
                    PromptHash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "timezone('utc', now())"),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCommentaryMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCommentaryMessages_AiCommentaryThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "AiCommentaryThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AiCommentaryMessageSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LawReportId = table.Column<int>(type: "integer", nullable: true),
                    LegalDocumentId = table.Column<int>(type: "integer", nullable: true),
                    PageNumber = table.Column<int>(type: "integer", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Citation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Snippet = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    LinkUrl = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiCommentaryMessageSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiCommentaryMessageSources_AiCommentaryMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "AiCommentaryMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "AiCommentarySettings",
                columns: new[] { "Id", "EnableUserHistory", "RetentionMonths", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, true, 6, new DateTime(2026, 2, 18, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryMessages_ThreadId_CreatedAtUtc",
                table: "AiCommentaryMessages",
                columns: new[] { "ThreadId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryMessages_ThreadId_IsDeleted_CreatedAtUtc",
                table: "AiCommentaryMessages",
                columns: new[] { "ThreadId", "IsDeleted", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryMessageSources_MessageId",
                table: "AiCommentaryMessageSources",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryMessageSources_Type_LawReportId",
                table: "AiCommentaryMessageSources",
                columns: new[] { "Type", "LawReportId" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryMessageSources_Type_LegalDocumentId_PageNumber",
                table: "AiCommentaryMessageSources",
                columns: new[] { "Type", "LegalDocumentId", "PageNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentarySettings_UpdatedByUserId",
                table: "AiCommentarySettings",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryThreads_IsDeleted_DeletedAtUtc",
                table: "AiCommentaryThreads",
                columns: new[] { "IsDeleted", "DeletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryThreads_LastActivityAtUtc",
                table: "AiCommentaryThreads",
                column: "LastActivityAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AiCommentaryThreads_UserId_IsDeleted_LastActivityAtUtc",
                table: "AiCommentaryThreads",
                columns: new[] { "UserId", "IsDeleted", "LastActivityAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiCommentaryMessageSources");

            migrationBuilder.DropTable(
                name: "AiCommentarySettings");

            migrationBuilder.DropTable(
                name: "AiCommentaryMessages");

            migrationBuilder.DropTable(
                name: "AiCommentaryThreads");

            migrationBuilder.AlterColumn<int>(
                name: "Kind",
                table: "LegalDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 1,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
