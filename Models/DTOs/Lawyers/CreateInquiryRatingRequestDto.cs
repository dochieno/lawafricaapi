using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.DTOs.Lawyers
{
    public class CreateInquiryRatingRequestDto
    {
        [Range(1, 5)]
        public int Stars { get; set; }

        public string? Comment { get; set; }
    }
}