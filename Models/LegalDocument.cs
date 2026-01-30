using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.LawReports.Enums;
using System.Xml.Linq;

public class LegalDocument
{
    public int Id { get; set; }

    // ---------------- IDENTITY ----------------
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    // ---------------- AUTHORSHIP ----------------
    public string? Author { get; set; }
    public string? Publisher { get; set; }
    public string? Edition { get; set; }

    // ---------------- CATEGORY ----------------
    public LegalDocumentCategory Category { get; set; }

    // ---------------- JURISDICTION ----------------
    public int CountryId { get; set; }
    public Country Country { get; set; } = null!;

    // ---------------- FILE METADATA ----------------
    public string FilePath { get; set; } = string.Empty;   // internal path
    public string FileType { get; set; } = "pdf";          // pdf | epub
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }                    // PDF
    public int? ChapterCount { get; set; }                 // EPUB

    // ---------------- BUSINESS ----------------
    public bool IsPremium { get; set; }
    public string Version { get; set; } = "1.0";

    // ---------------- LIFECYCLE ----------------
    public LegalDocumentStatus Status { get; set; } = LegalDocumentStatus.Draft;
    public DateTime? PublishedAt { get; set; }

    // ---------------- AUDIT ----------------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public string? CoverImagePath { get; set; }   // stored cover image (jpg/png) optional
    public string? FileHashSha256 { get; set; }   // integrity + cache validation
    public DateTime? LastIndexedAt { get; set; }  // if we later pre-process PDF/EPUB
    // ---------------- READER STRUCTURE ----------------
    public string? TableOfContentsJson { get; set; }
    // ---------------- PRODUCT ----------------
    public int? ContentProductId { get; set; }
    public ContentProduct? ContentProduct { get; set; }
 
    // ---------------- PRICING (PUBLIC) ----------------
    public decimal? PublicPrice { get; set; }           // e.g. 500.00
    public string? PublicCurrency { get; set; } = "KES"; // optional, default KES
    public bool AllowPublicPurchase { get; set; } = false; // gate purchase visibility/availability
    public LegalDocumentKind Kind { get; set; } = LegalDocumentKind.Standard;
    public LawAfrica.API.Models.Reports.LawReport? LawReport { get; set; }
    public int? VatRateId { get; set; }
    public bool IsTaxInclusive { get; set; }   // true = price includes VAT

    public LawAfrica.API.Models.Tax.VatRate? VatRate { get; set; }

    // ✅ 1:1 child (optional)
    public ICollection<ContentProductLegalDocument> ProductDocuments { get; set; } = new List<ContentProductLegalDocument>();
    public ICollection<LegalDocumentTocEntry> TocEntries { get; set; } = new List<LegalDocumentTocEntry>();



}
