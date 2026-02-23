using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLegalDocumentCategoryMetaAndSubCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_CountryId",
                table: "LegalDocuments");

            migrationBuilder.AddColumn<int>(
                name: "SubCategoryId",
                table: "LegalDocuments",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LegalDocumentCategoryMetas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentCategoryMetas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentSubCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CountryId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentSubCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LegalDocumentSubCategories_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                table: "LegalDocumentCategoryMetas",
                columns: new[] { "Id", "Code", "Description", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { 1, "commentaries", null, true, "Commentaries", 10 },
                    { 2, "international_titles", null, true, "International Titles", 20 },
                    { 3, "journals", null, true, "Journals", 30 },
                    { 4, "law_reports", null, true, "Law Reports", 40 },
                    { 5, "statutes", null, true, "Statutes", 50 },
                    { 6, "llr_services", null, true, "LLR Services", 60 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_Category_SubCategoryId",
                table: "LegalDocuments",
                columns: new[] { "Category", "SubCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_CountryId_Category_SubCategoryId",
                table: "LegalDocuments",
                columns: new[] { "CountryId", "Category", "SubCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_SubCategoryId",
                table: "LegalDocuments",
                column: "SubCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentCategoryMetas_Code",
                table: "LegalDocumentCategoryMetas",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentCategoryMetas_IsActive_SortOrder",
                table: "LegalDocumentCategoryMetas",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentSubCategories_CountryId",
                table: "LegalDocumentSubCategories",
                column: "CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_LegalDocuments_LegalDocumentSubCategories_SubCategoryId",
                table: "LegalDocuments",
                column: "SubCategoryId",
                principalTable: "LegalDocumentSubCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LegalDocuments_LegalDocumentSubCategories_SubCategoryId",
                table: "LegalDocuments");

            migrationBuilder.DropTable(
                name: "LegalDocumentCategoryMetas");

            migrationBuilder.DropTable(
                name: "LegalDocumentSubCategories");

            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_Category_SubCategoryId",
                table: "LegalDocuments");

            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_CountryId_Category_SubCategoryId",
                table: "LegalDocuments");

            migrationBuilder.DropIndex(
                name: "IX_LegalDocuments_SubCategoryId",
                table: "LegalDocuments");

            migrationBuilder.DropColumn(
                name: "SubCategoryId",
                table: "LegalDocuments");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocuments_CountryId",
                table: "LegalDocuments",
                column: "CountryId");
        }
    }
}
