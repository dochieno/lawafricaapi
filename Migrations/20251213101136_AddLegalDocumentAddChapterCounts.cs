using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentAddChapterCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverImageUrl",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "ISBN",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "IsDownloadable",
                table: "LegalDocuments");

            migrationBuilder.RenameColumn(
                name: "PublicationYear",
                table: "LegalDocuments",
                newName: "PageCount");

            migrationBuilder.RenameColumn(
                name: "FileUrl",
                table: "LegalDocuments",
                newName: "FilePath");

            migrationBuilder.AlterColumn<string>(
                name: "Publisher",
                table: "LegalDocuments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "FileType",
                table: "LegalDocuments",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<long>(
                name: "FileSizeBytes",
                table: "LegalDocuments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Author",
                table: "LegalDocuments",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "ChapterCount",
                table: "LegalDocuments",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChapterCount",
                table: "LegalDocuments");

            migrationBuilder.RenameColumn(
                name: "PageCount",
                table: "LegalDocuments",
                newName: "PublicationYear");

            migrationBuilder.RenameColumn(
                name: "FilePath",
                table: "LegalDocuments",
                newName: "FileUrl");

            migrationBuilder.AlterColumn<string>(
                name: "Publisher",
                table: "LegalDocuments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "FileType",
                table: "LegalDocuments",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<long>(
                name: "FileSizeBytes",
                table: "LegalDocuments",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<string>(
                name: "Author",
                table: "LegalDocuments",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CoverImageUrl",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ISBN",
                table: "LegalDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDownloadable",
                table: "LegalDocuments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
