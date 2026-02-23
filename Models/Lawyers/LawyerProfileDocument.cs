// FILE: LawAfrica.API/Models/Lawyers/LawyerProfileDocument.cs
using System.ComponentModel.DataAnnotations;

namespace LawAfrica.API.Models.Lawyers
{
    public enum LawyerDocumentType : short
    {
        Unknown = 0,
        KenyaSchoolOfLawCertificate = 1,
        AdmissionCertificate = 2,
        PracticingCertificate = 3,
        NationalIdOrPassport = 4,
        Other = 99
    }

    public class LawyerProfileDocument
    {
        public int Id { get; set; }

        public int LawyerProfileId { get; set; }
        public LawyerProfile LawyerProfile { get; set; } = null!;

        public LawyerDocumentType Type { get; set; } = LawyerDocumentType.Unknown;

        [MaxLength(180)]
        public string FileName { get; set; } = "";

        [MaxLength(120)]
        public string ContentType { get; set; } = "";

        public long SizeBytes { get; set; }

        // store like: /storage/LawyerDocs/lawyer_{profileId}/xxx.pdf
        [MaxLength(320)]
        public string UrlPath { get; set; } = "";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}