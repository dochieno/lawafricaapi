using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Trials;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/trials")]
    public class UserTrialsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UserTrialsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // -------------------------
        // Config
        // -------------------------
        private static int GetTrialDays()
        {
            var raw = Environment.GetEnvironmentVariable("TRIAL_DAYS");
            if (int.TryParse(raw, out var d))
            {
                if (d < 1) d = 1;
                if (d > 60) d = 60;
                return d;
            }
            return 7;
        }

        // ==========================
        // USER
        // ==========================

        /// <summary>
        /// User requests a trial (pending approval).
        /// Only allowed for products: AvailableToPublic=true AND PublicAccessModel=Subscription.
        /// Only allowed for public individual accounts (non-institution).
        /// </summary>
        [Authorize]
        [HttpPost("request")]
        public async Task<ActionResult<TrialRequestCreatedDto>> RequestTrial(
            [FromBody] RequestTrialDto dto,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var userId = User.GetUserId();

            // Product must be public + subscription
            var product = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.Id == dto.ContentProductId)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.AvailableToPublic,
                    p.PublicAccessModel
                })
                .FirstOrDefaultAsync(ct);

            if (product == null)
                return NotFound("Content product not found.");

            if (!product.AvailableToPublic)
                return BadRequest("This product is not available to public users.");

            if (product.PublicAccessModel != ProductAccessModel.Subscription)
                return BadRequest("Trials are only allowed for subscription products.");

            // Block institution users from trials
            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new { x.UserType, x.InstitutionId })
                .FirstOrDefaultAsync(ct);

            if (u == null) return Unauthorized();

            var isPublicIndividual = u.UserType == UserType.Public && u.InstitutionId == null;
            if (!isPublicIndividual)
                return BadRequest("Trials are only available for public individual accounts.");

            var now = DateTime.UtcNow;

            // If already has active subscription (trial or paid), deny
            var alreadyActive = await _db.UserProductSubscriptions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == userId &&
                    s.ContentProductId == dto.ContentProductId &&
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now && s.EndDate >= now,
                    ct);

            if (alreadyActive)
                return BadRequest("You already have an active subscription for this product.");

            // If already has a pending trial request, deny
            var pending = await _db.UserTrialSubscriptionRequests
                .AsNoTracking()
                .AnyAsync(r =>
                    r.UserId == userId &&
                    r.ContentProductId == dto.ContentProductId &&
                    r.Status == TrialRequestStatus.Pending,
                    ct);

            if (pending)
                return BadRequest("You already have a pending trial request for this product.");

            var req = new UserTrialSubscriptionRequest
            {
                UserId = userId,
                ContentProductId = dto.ContentProductId,
                Status = TrialRequestStatus.Pending,
                Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
                RequestIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            };

            _db.UserTrialSubscriptionRequests.Add(req);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // If you add the partial unique index, this becomes your safety net
                return Conflict("A pending trial request already exists for this product.");
            }

            return Ok(new TrialRequestCreatedDto
            {
                RequestId = req.Id,
                Status = req.Status.ToString(),
                ContentProductId = product.Id,
                ContentProductName = product.Name
            });
        }

        // ==========================
        // ADMIN (Global Admin only)
        // ==========================

        private async Task<User?> GetCurrentUserAsync(CancellationToken ct)
        {
            var userId = User.GetUserId();
            return await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        }

        private static bool IsGlobalAdmin(User? u) => u != null && u.IsGlobalAdmin;

        /// <summary>
        /// Admin lists trial requests (default: pending).
        /// </summary>
        [Authorize]
        [HttpGet("admin/requests")]
        public async Task<ActionResult<List<TrialRequestListItemDto>>> ListRequests(
            [FromQuery] TrialRequestStatus? status,
            CancellationToken ct)
        {
            var me = await GetCurrentUserAsync(ct);
            if (!IsGlobalAdmin(me)) return Forbid();

            var s = status ?? TrialRequestStatus.Pending;

            var items = await _db.UserTrialSubscriptionRequests
                .AsNoTracking()
                .Include(r => r.User)
                .Include(r => r.ContentProduct)
                .Where(r => r.Status == s)
                .OrderByDescending(r => r.RequestedAt)
                .Select(r => new TrialRequestListItemDto
                {
                    Id = r.Id,
                    Status = r.Status,
                    RequestedAt = r.RequestedAt,
                    ReviewedAt = r.ReviewedAt,
                    Reason = r.Reason,
                    AdminNotes = r.AdminNotes,
                    User = new TrialRequestUserDto
                    {
                        UserId = r.UserId,
                        Username = r.User.Username,
                        Email = r.User.Email,
                        PhoneNumber = r.User.PhoneNumber,
                        UserType = r.User.UserType,
                        InstitutionId = r.User.InstitutionId
                    },
                    Product = new TrialRequestProductDto
                    {
                        ContentProductId = r.ContentProductId,
                        Name = r.ContentProduct.Name
                    }
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        /// <summary>
        /// Admin approves: creates/resets UserProductSubscription as a trial.
        /// ✅ Safety: do not override an active paid subscription.
        /// </summary>
        [Authorize]
        [HttpPost("admin/requests/{requestId:int}/approve")]
        public async Task<ActionResult<TrialApprovalResultDto>> Approve(
            int requestId,
            [FromBody] ReviewTrialDto dto,
            CancellationToken ct)
        {
            var me = await GetCurrentUserAsync(ct);
            if (!IsGlobalAdmin(me)) return Forbid();

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var req = await _db.UserTrialSubscriptionRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);

            if (req == null) return NotFound("Trial request not found.");
            if (req.Status != TrialRequestStatus.Pending)
                return BadRequest("Only pending requests can be approved.");

            var now = DateTime.UtcNow;

            // ✅ Do NOT override an active PAID subscription
            var hasActivePaid = await _db.UserProductSubscriptions
                .AsNoTracking()
                .AnyAsync(s =>
                    s.UserId == req.UserId &&
                    s.ContentProductId == req.ContentProductId &&
                    s.Status == SubscriptionStatus.Active &&
                    s.IsTrial == false &&
                    s.StartDate <= now &&
                    s.EndDate >= now,
                    ct);

            if (hasActivePaid)
                return Conflict("User already has an active paid subscription for this product.");

            var days = GetTrialDays();

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(s => s.UserId == req.UserId && s.ContentProductId == req.ContentProductId, ct);

            if (sub == null)
            {
                sub = new UserProductSubscription
                {
                    UserId = req.UserId,
                    ContentProductId = req.ContentProductId,
                    Status = SubscriptionStatus.Active,
                    StartDate = now,
                    EndDate = now.AddDays(days),
                    IsTrial = true,
                    GrantedByUserId = me!.Id
                };
                _db.UserProductSubscriptions.Add(sub);
            }
            else
            {
                // reset to a clean trial window (safe because active paid is blocked above)
                sub.Status = SubscriptionStatus.Active;
                sub.IsTrial = true;
                sub.GrantedByUserId = me!.Id;
                sub.StartDate = now;
                sub.EndDate = now.AddDays(days);
            }

            req.Status = TrialRequestStatus.Approved;
            req.AdminNotes = string.IsNullOrWhiteSpace(dto?.AdminNotes) ? null : dto.AdminNotes!.Trim();
            req.ReviewedByUserId = me!.Id;
            req.ReviewedAt = now;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(new TrialApprovalResultDto
            {
                RequestId = req.Id,
                Subscription = new TrialSubscriptionDto
                {
                    Id = sub.Id,
                    UserId = sub.UserId,
                    ContentProductId = sub.ContentProductId,
                    Status = sub.Status,
                    IsTrial = sub.IsTrial,
                    StartDate = sub.StartDate,
                    EndDate = sub.EndDate
                }
            });
        }

        /// <summary>
        /// Admin rejects trial request.
        /// </summary>
        [Authorize]
        [HttpPost("admin/requests/{requestId:int}/reject")]
        public async Task<ActionResult<TrialReviewResultDto>> Reject(
            int requestId,
            [FromBody] ReviewTrialDto dto,
            CancellationToken ct)
        {
            var me = await GetCurrentUserAsync(ct);
            if (!IsGlobalAdmin(me)) return Forbid();

            var req = await _db.UserTrialSubscriptionRequests
                .FirstOrDefaultAsync(r => r.Id == requestId, ct);

            if (req == null) return NotFound("Trial request not found.");
            if (req.Status != TrialRequestStatus.Pending)
                return BadRequest("Only pending requests can be rejected.");

            req.Status = TrialRequestStatus.Rejected;
            req.AdminNotes = string.IsNullOrWhiteSpace(dto?.AdminNotes) ? null : dto.AdminNotes!.Trim();
            req.ReviewedByUserId = me!.Id;
            req.ReviewedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);

            return Ok(new TrialReviewResultDto
            {
                RequestId = req.Id,
                Status = req.Status.ToString()
            });
        }
    }
}
