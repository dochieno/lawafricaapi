using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.DTOs;
using LawAfrica.API.Models.DTOs.LegalDocuments;
using LawAfrica.API.Models.DTOs.Reader;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-documents")]
    public class LegalDocumentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<LegalDocumentsController> _logger;
        private const int DEFAULT_PREVIEW_MAX_PAGES = 20;

        public LegalDocumentsController(ApplicationDbContext db, ILogger<LegalDocumentsController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var docs = await _db.LegalDocuments
                .AsNoTracking()
                .Include(d => d.Country)
                .Where(d => d.Status == LegalDocumentStatus.Published)
                .Select(d => new LegalDocumentListDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    Description = d.Description,
                    Author = d.Author,
                    Publisher = d.Publisher,
                    Edition = d.Edition,
                    Category = d.Category.ToString(),
                    CountryId = d.CountryId,
                    CountryName = d.Country.Name,
                    FileType = d.FileType,
                    PageCount = d.PageCount,
                    ChapterCount = d.ChapterCount,
                    IsPremium = d.IsPremium,
                    Version = d.Version,
                    Status = d.Status.ToString(),
                    PublishedAt = d.PublishedAt,
                    CoverImagePath = d.CoverImagePath,
                    AllowPublicPurchase = d.AllowPublicPurchase,
                    PublicPrice = d.PublicPrice,
                    PublicCurrency = d.PublicCurrency
                })
                .ToListAsync();

            return Ok(docs);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .Include(d => d.Country)
                .Where(d => d.Id == id)
                .Select(d => new LegalDocumentDetailDto
                {
                    Id = d.Id,
                    Title = d.Title,
                    Description = d.Description,
                    Author = d.Author,
                    Publisher = d.Publisher,
                    Edition = d.Edition,
                    Category = d.Category.ToString(),
                    CountryId = d.CountryId,
                    CountryName = d.Country.Name,
                    FileType = d.FileType,
                    PageCount = d.PageCount,
                    ChapterCount = d.ChapterCount,
                    FileSizeBytes = d.FileSizeBytes,
                    IsPremium = d.IsPremium,
                    Version = d.Version,
                    Status = d.Status.ToString(),
                    PublishedAt = d.PublishedAt,
                    CreatedAt = d.CreatedAt,
                    CoverImagePath = d.CoverImagePath,
                    AllowPublicPurchase = d.AllowPublicPurchase,
                    PublicPrice = d.PublicPrice,
                    PublicCurrency = d.PublicCurrency
                })
                .FirstOrDefaultAsync();

            if (doc == null)
                return NotFound();

            return Ok(doc);
        }

        [Authorize(Policy = "RequireAdmin")]
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateLegalDocumentRequest request)
        {
            if (request == null)
                return BadRequest("Invalid request.");

            var countryExists = await _db.Countries.AnyAsync(c => c.Id == request.CountryId);
            if (!countryExists)
                return BadRequest("Invalid countryId.");

            var title = (request.Title ?? "").Trim();
            var description = (request.Description ?? "").Trim();
            var author = (request.Author ?? "").Trim();
            var publisher = (request.Publisher ?? "").Trim();
            var edition = (request.Edition ?? "").Trim();
            var filePath = (request.FilePath ?? "").Trim();
            var fileType = (request.FileType ?? "").Trim();
            var version = (request.Version ?? "").Trim();
            var publicCurrency = (request.PublicCurrency ?? "").Trim();

            var document = new LegalDocument
            {
                Title = title,
                Description = description,
                Author = author,
                Publisher = publisher,
                Edition = edition,

                Category = request.Category,

                CountryId = request.CountryId,
                FilePath = filePath,
                FileType = fileType,
                FileSizeBytes = request.FileSizeBytes,
                PageCount = request.PageCount,
                ChapterCount = request.ChapterCount,
                IsPremium = request.IsPremium,
                Version = version,
                Status = request.Status,
                PublishedAt = request.PublishedAt,
                PublicPrice = request.PublicPrice,
                AllowPublicPurchase = request.AllowPublicPurchase,
                PublicCurrency = publicCurrency,
                CreatedAt = DateTime.UtcNow
            };

            _db.LegalDocuments.Add(document);
            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Legal document created successfully",
                id = document.Id
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] LegalDocumentUpdateRequest request)
        {
            var doc = await _db.LegalDocuments.FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
                return NotFound("Legal document not found.");

            var countryExists = await _db.Countries.AnyAsync(c => c.Id == request.CountryId);
            if (!countryExists)
                return BadRequest("Invalid countryId.");

            doc.Title = request.Title.Trim();
            doc.Description = request.Description?.Trim();
            doc.Author = request.Author?.Trim();
            doc.Publisher = request.Publisher?.Trim();
            doc.Edition = request.Edition?.Trim();

            doc.Category = request.Category;
            doc.CountryId = request.CountryId;

            doc.IsPremium = request.IsPremium;
            doc.Version = request.Version;
            doc.Status = request.Status;
            doc.PublishedAt = request.PublishedAt;
            doc.AllowPublicPurchase = request.AllowPublicPurchase;
            doc.PublicCurrency = request.PublicCurrency;
            doc.PublicPrice = request.PublicPrice;
            doc.PageCount = request.PageCount;

            doc.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "Legal document updated successfully.",
                documentId = doc.Id
            });
        }

        // ✅ Upload Ebook (SAFE): accepts form keys "File" or "file" and avoids null => 500
        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/upload")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(200_000_000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 200_000_000)]
        public async Task<IActionResult> UploadEbook(
            int id,
            [FromForm] UploadLegalDocumentFileRequest? request,
            [FromForm] IFormFile? file,
            [FromForm(Name = "File")] IFormFile? File,
            [FromServices] FileStorageService storage)
        {
            try
            {
                var doc = await _db.LegalDocuments.FindAsync(id);
                if (doc == null)
                    return NotFound("Document not found.");

                var f = request?.File ?? file ?? File;
                if (f == null || f.Length == 0)
                    return BadRequest(new
                    {
                        message = "No ebook file received. Use multipart/form-data with key 'File' (recommended) or 'file'."
                    });

                var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
                if (ext != ".pdf" && ext != ".epub")
                    return BadRequest("Only PDF or EPUB files are allowed.");

                var fileType = ext.Replace(".", "");

                var (path, size) = await storage.SaveLegalDocumentAsync(f, fileType);

                doc.FilePath = path;
                doc.FileType = fileType;
                doc.FileSizeBytes = size;
                doc.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                return Ok(new
                {
                    message = "Ebook uploaded successfully",
                    Id = doc.Id,
                    FileType = doc.FileType,
                    FileSizeBytes = doc.FileSizeBytes,
                    FilePath = doc.FilePath
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UploadEbook failed for LegalDocumentId={Id}", id);
                return StatusCode(500, new { message = "Ebook upload failed on server. Check server logs for details." });
            }
        }

        // ✅ Download Endpoint (SINGLE SOURCE OF TRUTH = DocumentEntitlementService)
        [Authorize]
        [HttpGet("{id}/download")]
        public async Task<IActionResult> Download(
            int id,
            [FromServices] DocumentEntitlementService entitlementService,
            [FromServices] FileStorageService storage)
        {
            var doc = await _db.LegalDocuments.FindAsync(id);
            if (doc == null || string.IsNullOrWhiteSpace(doc.FilePath))
                return NotFound("Document not found or file missing.");

            var userId = User.GetUserId();

            // ✅ SINGLE SOURCE OF TRUTH
            var decision = await entitlementService.GetEntitlementDecisionAsync(userId, doc);

            // ✅ Hard blocks: institution inactive or seat exceeded
            if (!decision.IsAllowed &&
                (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                 decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded))
            {
                Response.Headers["X-Entitlement-Deny-Reason"] = decision.DenyReason.ToString();
                if (!string.IsNullOrWhiteSpace(decision.Message))
                    Response.Headers["X-Entitlement-Message"] = decision.Message;

                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    message = decision.Message ?? "Access blocked. Please contact your administrator.",
                    denyReason = decision.DenyReason.ToString(),
                    canPurchaseIndividually = decision.CanPurchaseIndividually,
                    purchaseDisabledReason = decision.PurchaseDisabledReason
                });
            }

            // ✅ Not entitled: return 403 (frontend can show purchase flow / preview messaging)
            // NOTE: react-pdf still needs the file for PreviewOnly.
            // Your current system implements preview by limiting pages in the UI (DEFAULT_PREVIEW_MAX_PAGES),
            // so we allow PreviewOnly to load the file.
            //
            // If you ever want server-side preview (first N pages only), that would require additional processing.
            if (decision.DenyReason == DocumentEntitlementDenyReason.NotEntitled)
            {
                // Allow PreviewOnly to read the file (UI will limit pages).
                // If you want NotEntitled to be blocked entirely, change this to a 403 return.
            }

            var physicalPath = storage.ResolvePhysicalPathFromDbPath(doc.FilePath);
            if (!System.IO.File.Exists(physicalPath))
                return NotFound("File missing on server.");

            var contentType = doc.FileType switch
            {
                "pdf" => "application/pdf",
                "epub" => "application/epub+zip",
                _ => "application/octet-stream"
            };

            Response.Headers.Append("Content-Disposition", $"inline; filename=\"{Path.GetFileName(physicalPath)}\"");
            Response.Headers.Append("X-Document-Access",
                decision.AccessLevel == DocumentAccessLevel.FullAccess ? "Full" : "Preview");

            return PhysicalFile(physicalPath, contentType, enableRangeProcessing: true);
        }

        // ✅ Upload Cover (SAFE): accepts form keys "file" or "File"
        [Authorize(Roles = "Admin")]
        [HttpPost("{id}/cover")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(50_000_000)]       
        [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]        
        public async Task<IActionResult> UploadCover(
            int id,
            [FromForm] IFormFile? file,
            [FromForm(Name = "File")] IFormFile? File,
            [FromServices] FileStorageService storage)
        {
            try
            {
                var f = file ?? File;
                if (f == null || f.Length == 0)
                    return BadRequest("Cover file is required.");

                var doc = await _db.LegalDocuments.FindAsync(id);
                if (doc == null)
                    return NotFound("Document not found.");

                var (relativePath, size) = await storage.SaveCoverAsync(f);

                doc.CoverImagePath = relativePath;
                doc.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                var clean = relativePath.Replace("\\", "/");
                clean = clean.Replace("Storage/", "", StringComparison.OrdinalIgnoreCase).TrimStart('/');
                var coverUrl = $"{Request.Scheme}://{Request.Host}/storage/{clean}";

                return Ok(new
                {
                    message = "Cover uploaded successfully.",
                    CoverImagePath = doc.CoverImagePath,
                    coverUrl,
                    size
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "UploadCover failed for LegalDocumentId={Id}", id);
                return StatusCode(500, new { message = "Cover upload failed on server. Check server logs for details." });
            }
        }

        [HttpGet("{id:int}/toc")]
        public async Task<IActionResult> GetTableOfContents(int id)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new { d.TableOfContentsJson })
                .FirstOrDefaultAsync();

            if (doc == null)
                return NotFound();

            var tocItems = TocParser.ParseOrEmpty(doc.TableOfContentsJson);
            return Ok(new { items = tocItems });
        }

        [HttpGet("{id}/access")]
        [Authorize]
        public async Task<IActionResult> GetAccess(int id, [FromServices] DocumentEntitlementService entitlementService)
        {
            var userId = User.GetUserId();

            var userCtx = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.InstitutionId })
                .FirstOrDefaultAsync();

            if (userCtx == null)
                return Unauthorized();

            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null)
                return NotFound("Document not found.");

            if (!doc.IsPremium)
            {
                return Ok(new DocumentAccessDto
                {
                    DocumentId = id,
                    IsPremium = false,
                    HasFullAccess = true,
                    PreviewMaxPages = int.MaxValue,
                    Message = "Free document. Full access granted.",
                    IsBlocked = false,
                    BlockMessage = null,
                    BlockReason = null,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                });
            }

            var decision = await entitlementService.GetEntitlementDecisionAsync(userId, doc);

            bool canPurchaseIndividually = decision.CanPurchaseIndividually;
            string? purchaseDisabledReason = decision.PurchaseDisabledReason;

            if (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded)
            {
                return Ok(new DocumentAccessDto
                {
                    DocumentId = id,
                    IsPremium = true,
                    HasFullAccess = false,
                    PreviewMaxPages = 0,
                    Message = decision.Message ?? "Access blocked.",
                    IsBlocked = true,
                    BlockReason = decision.DenyReason.ToString(),
                    BlockMessage = decision.Message ?? "Access blocked. Please contact your administrator.",
                    CanPurchaseIndividually = canPurchaseIndividually,
                    PurchaseDisabledReason = purchaseDisabledReason
                });
            }

            if (decision.AccessLevel == DocumentAccessLevel.FullAccess)
            {
                return Ok(new DocumentAccessDto
                {
                    DocumentId = id,
                    IsPremium = true,
                    HasFullAccess = true,
                    PreviewMaxPages = int.MaxValue,
                    Message = "Premium document. Full access granted.",
                    IsBlocked = false,
                    BlockMessage = null,
                    BlockReason = null,
                    CanPurchaseIndividually = true,
                    PurchaseDisabledReason = null
                });
            }

            // Preview-only
            return Ok(new DocumentAccessDto
            {
                DocumentId = id,
                IsPremium = true,
                HasFullAccess = false,
                PreviewMaxPages = DEFAULT_PREVIEW_MAX_PAGES,
                Message = $"Preview mode: first {DEFAULT_PREVIEW_MAX_PAGES} pages available.",
                IsBlocked = false,
                BlockMessage = null,
                BlockReason = null,
                CanPurchaseIndividually = canPurchaseIndividually,
                PurchaseDisabledReason = purchaseDisabledReason
            });
        }

        [HttpGet("{id:int}/availability")]
        public async Task<IActionResult> GetAvailability(int id, [FromServices] FileStorageService storage)
        {
            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new { d.Id, d.FilePath })
                .FirstOrDefaultAsync();

            if (doc == null)
                return NotFound("Document not found.");

            if (string.IsNullOrWhiteSpace(doc.FilePath))
            {
                return Ok(new LegalDocumentAvailabilityDto
                {
                    DocumentId = id,
                    HasContent = false,
                    Message = "Great news! This document is in our catalog, but the content isn’t ready just yet. Check back soon we are working on it!"
                });
            }

            var physicalPath = storage.ResolvePhysicalPathFromDbPath(doc.FilePath);

            if (!System.IO.File.Exists(physicalPath))
            {
                return Ok(new LegalDocumentAvailabilityDto
                {
                    DocumentId = id,
                    HasContent = false,
                    Message = "Great news! This document is in our catalog, but the content isn’t ready just yet. Check back soon we are working on it!"
                });
            }

            return Ok(new LegalDocumentAvailabilityDto
            {
                DocumentId = id,
                HasContent = true,
                Message = "Document content is available."
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpGet("admin")]
        public async Task<IActionResult> GetAllForAdmin()
        {
            var docs = await _db.LegalDocuments
                .AsNoTracking()
                .Include(d => d.Country)
                .OrderByDescending(d => d.CreatedAt)
                .Select(d => new
                {
                    d.Id,
                    d.Title,
                    d.Status,
                    d.IsPremium,
                    d.PageCount,
                    d.CreatedAt,
                    d.AllowPublicPurchase,
                    d.PublicCurrency,
                    d.PublicPrice,
                    d.CoverImagePath
                })
                .ToListAsync();

            return Ok(docs);
        }

        [Authorize]
        [HttpGet("{id:int}/public-offer")]
        public async Task<IActionResult> GetPublicOffer(int id, [FromServices] DocumentEntitlementService entitlementService)
        {
            var userId = User.GetUserId();

            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Id, u.UserType, u.InstitutionId })
                .FirstOrDefaultAsync();

            if (user == null)
                return Unauthorized();

            var isPublicIndividual = user.UserType == UserType.Public && user.InstitutionId == null;
            var isAdmin = user.UserType == UserType.Admin;
            bool isInstitutionUser = user.InstitutionId != null;

            if (!isPublicIndividual && !isAdmin && !isInstitutionUser)
            {
                return Ok(new PublicDocumentOfferDto
                {
                    LegalDocumentId = id,
                    AllowPublicPurchase = false,
                    Price = null,
                    Currency = null,
                    AlreadyOwned = false,
                    Message = "Pricing is not available for this account."
                });
            }

            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == LegalDocumentStatus.Published);

            if (doc == null)
                return NotFound("Document not found.");

            if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
            {
                return Ok(new PublicDocumentOfferDto
                {
                    LegalDocumentId = id,
                    AllowPublicPurchase = false,
                    Price = doc.PublicPrice,
                    Currency = doc.PublicCurrency,
                    AlreadyOwned = false,
                    Message = "This document is not available for individual purchase (or price not set)."
                });
            }

            if (isAdmin)
            {
                return Ok(new PublicDocumentOfferDto
                {
                    LegalDocumentId = id,
                    AllowPublicPurchase = true,
                    Price = doc.PublicPrice,
                    Currency = doc.PublicCurrency,
                    AlreadyOwned = false,
                    Message = "Admin view: purchase offer available."
                });
            }

            if (isInstitutionUser)
            {
                var instPolicy = await _db.Institutions
                    .AsNoTracking()
                    .Where(i => i.Id == user.InstitutionId!.Value)
                    .Select(i => new
                    {
                        i.IsActive,
                        i.AllowIndividualPurchasesWhenInstitutionInactive
                    })
                    .FirstOrDefaultAsync();

                bool allowWhenBlocked = instPolicy?.AllowIndividualPurchasesWhenInstitutionInactive ?? false;
                bool instActive = instPolicy?.IsActive ?? true;

                var decision = await entitlementService.GetEntitlementDecisionAsync(userId, doc);

                bool blocked =
                    decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                    decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded ||
                    !instActive;

                if (blocked && !allowWhenBlocked)
                {
                    return Ok(new PublicDocumentOfferDto
                    {
                        LegalDocumentId = id,
                        AllowPublicPurchase = false,
                        Price = doc.PublicPrice,
                        Currency = doc.PublicCurrency,
                        AlreadyOwned = false,
                        Message = "Purchases are disabled for institution accounts. Please contact your administrator."
                    });
                }

                if (!blocked && decision.AccessLevel == DocumentAccessLevel.FullAccess)
                {
                    return Ok(new PublicDocumentOfferDto
                    {
                        LegalDocumentId = id,
                        AllowPublicPurchase = false,
                        Price = doc.PublicPrice,
                        Currency = doc.PublicCurrency,
                        AlreadyOwned = false,
                        Message = "This document is included in your institution subscription."
                    });
                }
            }

            var alreadyOwned = await _db.UserLegalDocumentPurchases
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId && p.LegalDocumentId == id);

            return Ok(new PublicDocumentOfferDto
            {
                LegalDocumentId = id,
                AllowPublicPurchase = true,
                Price = doc.PublicPrice,
                Currency = doc.PublicCurrency,
                AlreadyOwned = alreadyOwned,
                Message = alreadyOwned ? "You already own this document." : "Available for purchase."
            });
        }

        [Authorize]
        [HttpGet("{id:int}/entitlement")]
        public async Task<IActionResult> GetEntitlement(int id, [FromServices] DocumentEntitlementService entitlementService)
        {
            var userId = User.GetUserId();

            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.Id,
                    u.UserType,
                    u.InstitutionId
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return Unauthorized();

            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id && d.Status == LegalDocumentStatus.Published);

            if (doc == null)
                return NotFound("Document not found.");

            var decision = await entitlementService.GetEntitlementDecisionAsync(userId, doc);

            var hasFullAccess = !doc.IsPremium || decision.AccessLevel == DocumentAccessLevel.FullAccess;

            string accessSource;
            if (!doc.IsPremium) accessSource = "Free";
            else if (hasFullAccess)
                accessSource = (user.InstitutionId != null) ? "Institution" : "Purchase";
            else accessSource = "Preview";

            var isBlocked =
                !decision.IsAllowed &&
                (decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSubscriptionInactive ||
                 decision.DenyReason == DocumentEntitlementDenyReason.InstitutionSeatLimitExceeded);

            return Ok(new
            {
                legalDocumentId = id,
                isPremium = doc.IsPremium,
                accessLevel = decision.AccessLevel.ToString(),
                hasFullAccess = hasFullAccess,
                previewMaxPages = hasFullAccess ? int.MaxValue : DEFAULT_PREVIEW_MAX_PAGES,

                userType = user.UserType.ToString(),
                institutionId = user.InstitutionId,

                accessSource,

                isBlocked,
                denyReason = decision.DenyReason.ToString(),
                message = isBlocked
                    ? (decision.Message ?? "Access blocked. Please contact your administrator.")
                    : (hasFullAccess ? "Access granted." : $"Preview mode: first {DEFAULT_PREVIEW_MAX_PAGES} pages available.")
            });
        }
    }
}
