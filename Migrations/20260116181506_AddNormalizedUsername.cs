using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1️⃣ Add column (nullable first to avoid breaking existing rows)
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            // 2️⃣ Backfill existing users (YOUR SQL — correct)
            migrationBuilder.Sql(@"
        UPDATE ""Users""
        SET ""NormalizedUsername"" = UPPER(TRIM(COALESCE(""Username"", '')))
        WHERE ""NormalizedUsername"" IS NULL;
    ");

            // 3️⃣ Make column NOT NULL after backfill
            migrationBuilder.AlterColumn<string>(
                name: "NormalizedUsername",
                table: "Users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            // 4️⃣ Add unique index (case-insensitive guarantee)
            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUsername",
                table: "Users",
                column: "NormalizedUsername",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedUsername",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "Users");
        }

    }
}
