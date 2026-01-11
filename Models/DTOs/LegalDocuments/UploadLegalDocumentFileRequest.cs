using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.DTOs
{
    public class UploadLegalDocumentFileRequest
    {
        [Required]
        public IFormFile File { get; set; } = null!;
    }
}
