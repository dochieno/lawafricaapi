using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddLawReportsTrigramSearch : Migration
    {
        /// <inheritdoc />
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql(@"CREATE EXTENSION IF NOT EXISTS pg_trgm;");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_ContentText_trgm""
                    ON ""LawReports""
                    USING gin (""ContentText"" gin_trgm_ops);
                ");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_Parties_trgm""
                    ON ""LawReports""
                    USING gin (""Parties"" gin_trgm_ops);
                ");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_Citation_trgm""
                    ON ""LawReports""
                    USING gin (""Citation"" gin_trgm_ops);
                ");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_Court_trgm""
                    ON ""LawReports""
                    USING gin (""Court"" gin_trgm_ops);
                ");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_CaseNumber_trgm""
                    ON ""LawReports""
                    USING gin (""CaseNumber"" gin_trgm_ops);
                ");

                migrationBuilder.Sql(@"
                    CREATE INDEX IF NOT EXISTS ""IX_LawReports_ReportNumber_trgm""
                    ON ""LawReports""
                    USING gin (""ReportNumber"" gin_trgm_ops);
                ");
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_ReportNumber_trgm"";");
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_CaseNumber_trgm"";");
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_Court_trgm"";");
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_Citation_trgm"";");
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_Parties_trgm"";");
                migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_LawReports_ContentText_trgm"";");
            }

        /// <inheritdoc />

    }
}
