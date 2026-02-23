using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class UpdateInquiryStatusRequestDto
    {
        [Required]
        public string Status { get; set; } = ""; // New | Contacted | InProgress | Closed | Spam

        public string? Outcome { get; set; } // Resolved | NotResolved | NoResponse | Declined | Duplicate
        public string? Note { get; set; }
    }
}