using LawAfrica.API.DTOs.Reports;
using Microsoft.AspNetCore.Http;

namespace LawAfrica.API.Controllers
{
    public static class ReportImportParsers
    {
        // TODO: implement with EPPlus or NPOI
        // Expected columns (suggested):
        // ReportNumber | Year | CaseNumber | Citation | Parties | Court | Judges | DecisionType | CaseType | DecisionDate | ContentText
        public static Task<List<ReportImportPreviewItemDto>> ParseExcelAsync(IFormFile file)
        {
            // Placeholder: return empty list to force wiring
            // Implement by reading workbook rows.
            return Task.FromResult(new List<ReportImportPreviewItemDto>());
        }

        // Word: extract text from docx; metadata passed in (reportNumber/year),
        // optionally you can expand later to parse headings for fields.
        public static async Task<ReportImportPreviewItemDto> ParseWordAsync(IFormFile file, string reportNumber, int year)
        {
            var text = await ExtractDocxTextAsync(file);

            return new ReportImportPreviewItemDto
            {
                RowNumber = 1,
                ReportNumber = reportNumber?.Trim() ?? "",
                Year = year,
                ContentText = text,
                DecisionType = null,
                CaseType = null
            };
        }

        private static async Task<string> ExtractDocxTextAsync(IFormFile file)
        {
            // Minimal, no external deps here.
            // If you already use DocumentFormat.OpenXml, replace this stub with real extraction.
            // For now: store as empty => your preview will mark invalid, prompting you to implement extraction.
            await Task.CompletedTask;
            return "";
        }
    }
}
