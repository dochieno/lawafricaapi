using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLawReportAttachmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AttachmentFileSizeBytes",
                table: "LawReports",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentFileType",
                table: "LawReports",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentOriginalName",
                table: "LawReports",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AttachmentPath",
                table: "LawReports",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttachmentFileSizeBytes",
                table: "LawReports");

            migrationBuilder.DropColumn(
                name: "AttachmentFileType",
                table: "LawReports");

            migrationBuilder.DropColumn(
                name: "AttachmentOriginalName",
                table: "LawReports");

            migrationBuilder.DropColumn(
                name: "AttachmentPath",
                table: "LawReports");
        }
    }
}
