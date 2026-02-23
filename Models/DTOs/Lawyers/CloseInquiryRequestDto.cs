using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class CloseInquiryRequestDto
    {
        [Required]
        public string Outcome { get; set; } = ""; // required

        public string? Note { get; set; }
    }
}