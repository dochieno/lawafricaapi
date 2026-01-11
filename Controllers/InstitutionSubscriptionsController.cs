using System.Security.Claims;
using LawAfrica.API.Authorization.Policies;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Subscriptions;
using LawAfrica.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/institutions/subscriptions")]
    [Authorize(Roles = "Admin")]
    public class InstitutionSubscriptionsController : ControllerBase
    {
        private readonly InstitutionSubscriptionService _subscriptionService;
        private readonly ApplicationDbContext _db;

        public InstitutionSubscriptionsController(
            InstitutionSubscriptionService subscriptionService,
            ApplicationDbContext db)
        {
            _subscriptionService = subscriptionService;
            _db = db;
        }

        private int? GetUserId()
        {
            var raw =
                User.FindFirstValue("userId") ??
                User.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(raw, out var id) ? id : null;
        }

        /// <summary>
        /// List all institution product subscriptions.
        /// ✅ Includes pending request summary for UX ("Suspend pending", etc.)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            // pending request for each subscription (at most 1 pending at a time by your service rules)
            var pendingQuery = _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Where(r => r.Status == SubscriptionActionRequestStatus.Pending);

            var rows = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .Include(s => s.Institution)
                .Include(s => s.ContentProduct)
                .OrderByDescending(s => s.Id)
                .Select(s => new
                {
                    Sub = s,
                    Pending = pendingQuery
                        .Where(r => r.SubscriptionId == s.Id)
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => new
                        {
                            r.Id,
                            r.RequestType,
                            r.CreatedAt,
                            r.RequestedByUserId
                        })
                        .FirstOrDefault()
                })
                .Select(x => new InstitutionSubscriptionDto
                {
                    Id = x.Sub.Id,
                    InstitutionId = x.Sub.InstitutionId,
                    InstitutionName = x.Sub.Institution.Name,
                    ContentProductId = x.Sub.ContentProductId,
                    ContentProductName = x.Sub.ContentProduct.Name,
                    Status = x.Sub.Status,
                    StartDate = x.Sub.StartDate,
                    EndDate = x.Sub.EndDate,

                    PendingRequestId = x.Pending != null ? x.Pending.Id : null,
                    PendingRequestType = x.Pending != null ? x.Pending.RequestType : null,
                    PendingRequestedAt = x.Pending != null ? x.Pending.CreatedAt : null,
                    PendingRequestedByUserId = x.Pending != null ? x.Pending.RequestedByUserId : null
                })
                .ToListAsync();

            return Ok(rows);
        }

        /// <summary>
        /// Get a subscription by Id.
        /// ✅ Includes pending request summary.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetById([FromRoute] int id)
        {
            var pendingQuery = _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Where(r => r.Status == SubscriptionActionRequestStatus.Pending);

            var row = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .Include(x => x.Institution)
                .Include(x => x.ContentProduct)
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    Sub = x,
                    Pending = pendingQuery
                        .Where(r => r.SubscriptionId == x.Id)
                        .OrderByDescending(r => r.CreatedAt)
                        .Select(r => new
                        {
                            r.Id,
                            r.RequestType,
                            r.CreatedAt,
                            r.RequestedByUserId
                        })
                        .FirstOrDefault()
                })
                .Select(x => new InstitutionSubscriptionDto
                {
                    Id = x.Sub.Id,
                    InstitutionId = x.Sub.InstitutionId,
                    InstitutionName = x.Sub.Institution.Name,
                    ContentProductId = x.Sub.ContentProductId,
                    ContentProductName = x.Sub.ContentProduct.Name,
                    Status = x.Sub.Status,
                    StartDate = x.Sub.StartDate,
                    EndDate = x.Sub.EndDate,

                    PendingRequestId = x.Pending != null ? x.Pending.Id : null,
                    PendingRequestType = x.Pending != null ? x.Pending.RequestType : null,
                    PendingRequestedAt = x.Pending != null ? x.Pending.CreatedAt : null,
                    PendingRequestedByUserId = x.Pending != null ? x.Pending.RequestedByUserId : null
                })
                .FirstOrDefaultAsync();

            if (row == null) return NotFound("Subscription not found.");
            return Ok(row);
        }

        /// <summary>
        /// Audit trail for a subscription.
        /// </summary>
        [HttpGet("{id:int}/audit")]
        public async Task<IActionResult> GetAudit([FromRoute] int id)
        {
            var exists = await _db.InstitutionProductSubscriptions.AnyAsync(x => x.Id == id);
            if (!exists) return NotFound("Subscription not found.");

            var rows = await _db.InstitutionSubscriptionAudits
                .AsNoTracking()
                .Where(a => a.SubscriptionId == id)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new InstitutionSubscriptionAuditDto
                {
                    Id = a.Id,
                    SubscriptionId = a.SubscriptionId,
                    Action = a.Action,
                    PerformedByUserId = a.PerformedByUserId,
                    OldStartDate = a.OldStartDate,
                    OldEndDate = a.OldEndDate,
                    OldStatus = a.OldStatus,
                    NewStartDate = a.NewStartDate,
                    NewEndDate = a.NewEndDate,
                    NewStatus = a.NewStatus,
                    Notes = a.Notes,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }

        /// <summary>
        /// ✅ NEW: Request history for a subscription (Admin can view logs).
        /// This is NOT the review endpoint; it's only for visibility/UX.
        /// </summary>
        [HttpGet("{id:int}/requests")]
        public async Task<IActionResult> GetRequestsForSubscription([FromRoute] int id)
        {
            var exists = await _db.InstitutionProductSubscriptions.AnyAsync(x => x.Id == id);
            if (!exists) return NotFound(new { message = "Subscription not found." });

            var users = _db.Users.AsNoTracking();

            var rows = await _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.Institution)
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.ContentProduct)
                .Where(r => r.SubscriptionId == id)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new SubscriptionActionRequestListDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,

                    InstitutionId = r.Subscription.InstitutionId,
                    InstitutionName = r.Subscription.Institution.Name,

                    ContentProductId = r.Subscription.ContentProductId,
                    ContentProductName = r.Subscription.ContentProduct.Name,

                    RequestType = r.RequestType,
                    Status = r.Status,

                    RequestedByUserId = r.RequestedByUserId,
                    RequestedByUsername = users
                        .Where(u => u.Id == r.RequestedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault() ?? "",

                    RequestNotes = r.RequestNotes,

                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUserId.HasValue
                        ? (users.Where(u => u.Id == r.ReviewedByUserId.Value).Select(u => u.Username).FirstOrDefault() ?? "")
                        : null,

                    ReviewNotes = r.ReviewNotes,
                    ReviewedAt = r.ReviewedAt,

                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }

        [HttpPost]
        public async Task<IActionResult> CreateSubscription([FromBody] CreateInstitutionSubscriptionRequest request)
        {
            if (request == null) return BadRequest("Request body is required.");
            if (request.InstitutionId <= 0) return BadRequest("InstitutionId must be > 0.");
            if (request.ContentProductId <= 0) return BadRequest("ContentProductId must be > 0.");
            if (request.DurationInMonths <= 0) return BadRequest("DurationInMonths must be > 0.");

            DateTime? startDate = null;
            if (request.StartDate.HasValue)
            {
                var dt = request.StartDate.Value;
                if (dt.Kind == DateTimeKind.Unspecified)
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                else if (dt.Kind == DateTimeKind.Local)
                    dt = dt.ToUniversalTime();

                startDate = dt;
            }

            var sub = await _subscriptionService.CreateOrExtendSubscriptionAsync(
                request.InstitutionId,
                request.ContentProductId,
                request.DurationInMonths,
                startDate,
                GetUserId()
            );

            return Ok(new
            {
                message = sub.Status == SubscriptionStatus.Pending
                    ? "Institution subscription scheduled (Pending)."
                    : "Institution subscription saved.",
                subscriptionId = sub.Id,
                sub.StartDate,
                sub.EndDate,
                sub.Status
            });
        }

        [HttpPost("{id:int}/renew")]
        public async Task<IActionResult> Renew([FromRoute] int id, [FromBody] RenewInstitutionSubscriptionRequest request)
        {
            if (request == null) return BadRequest("Request body is required.");
            if (request.DurationInMonths <= 0) return BadRequest("DurationInMonths must be > 0.");

            var existing = await _db.InstitutionProductSubscriptions
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);

            if (existing == null) return NotFound("Subscription not found.");

            if (existing.Status == SubscriptionStatus.Suspended)
            {
                return Conflict(new
                {
                    message = "Cannot renew a suspended subscription. Unsuspend it first.",
                    subscriptionId = existing.Id,
                    existing.Status,
                    existing.StartDate,
                    existing.EndDate
                });
            }

            DateTime? startDate = null;
            if (request.StartDate.HasValue)
            {
                var dt = request.StartDate.Value;

                if (dt.Kind == DateTimeKind.Unspecified)
                    dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                else if (dt.Kind == DateTimeKind.Local)
                    dt = dt.ToUniversalTime();

                startDate = dt;
            }

            var sub = await _subscriptionService.RenewSubscriptionAsync(
                id,
                request.DurationInMonths,
                startDate,
                GetUserId()
            );

            return Ok(new
            {
                message = "Subscription renewed.",
                subscriptionId = sub.Id,
                sub.StartDate,
                sub.EndDate,
                sub.Status
            });
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/suspend")]
        public async Task<IActionResult> Suspend([FromRoute] int id)
        {
            try
            {
                var sub = await _subscriptionService.SuspendAsync(id, GetUserId());
                return Ok(new { message = "Subscription suspended.", sub.Id, sub.Status, sub.StartDate, sub.EndDate });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("{id:int}/unsuspend")]
        public async Task<IActionResult> Unsuspend([FromRoute] int id)
        {
            try
            {
                var sub = await _subscriptionService.UnsuspendAsync(id, GetUserId());
                return Ok(new { message = "Subscription updated.", sub.Id, sub.Status, sub.StartDate, sub.EndDate });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(ex.Message);
            }
        }

        [HttpPost("{id:int}/request-suspend")]
        public async Task<IActionResult> RequestSuspend([FromRoute] int id, [FromBody] SubscriptionActionRequestDto dto)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Unauthorized();

            try
            {
                var req = await _subscriptionService.RequestSuspendAsync(id, userId.Value, dto?.Notes);
                return Ok(new { message = "Suspend request submitted for approval.", requestId = req.Id });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("{id:int}/request-unsuspend")]
        public async Task<IActionResult> RequestUnsuspend([FromRoute] int id, [FromBody] SubscriptionActionRequestDto dto)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Unauthorized();

            try
            {
                var req = await _subscriptionService.RequestUnsuspendAsync(id, userId.Value, dto?.Notes);
                return Ok(new { message = "Unsuspend request submitted for approval.", requestId = req.Id });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpPost("requests/{requestId:int}/review")]
        public async Task<IActionResult> ReviewRequest([FromRoute] int requestId, [FromBody] ReviewSubscriptionActionRequestDto dto)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Unauthorized();

            try
            {
                var req = await _subscriptionService.ApproveRequestAsync(
                    requestId,
                    userId.Value,
                    dto.Approve,
                    dto.Notes
                );

                return Ok(new
                {
                    message = dto.Approve ? "Request approved and applied." : "Request rejected.",
                    requestId = req.Id,
                    status = req.Status.ToString()
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        // ✅ Your existing GlobalAdmin-only list endpoint stays as-is:
        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpGet("requests")]
        public async Task<IActionResult> GetRequests([FromQuery] string? status = "pending", [FromQuery] string? q = null)
        {
            var statusNorm = (status ?? "pending").Trim().ToLowerInvariant();
            var qNorm = (q ?? "").Trim().ToLowerInvariant();

            var query = _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.Institution)
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.ContentProduct)
                .OrderByDescending(r => r.Id)
                .AsQueryable();

            if (statusNorm != "all")
            {
                if (statusNorm == "pending")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending);
                else if (statusNorm == "approved")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Approved);
                else if (statusNorm == "rejected")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Rejected);
                else
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending);
            }

            var users = _db.Users.AsNoTracking();

            var rows = await query
                .Select(r => new SubscriptionActionRequestListDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,

                    InstitutionId = r.Subscription.InstitutionId,
                    InstitutionName = r.Subscription.Institution.Name,

                    ContentProductId = r.Subscription.ContentProductId,
                    ContentProductName = r.Subscription.ContentProduct.Name,

                    RequestType = r.RequestType,
                    Status = r.Status,

                    RequestedByUserId = r.RequestedByUserId,
                    RequestedByUsername = users
                        .Where(u => u.Id == r.RequestedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault() ?? "",

                    RequestNotes = r.RequestNotes,

                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUserId.HasValue
                        ? (users.Where(u => u.Id == r.ReviewedByUserId.Value).Select(u => u.Username).FirstOrDefault() ?? "")
                        : null,

                    ReviewNotes = r.ReviewNotes,
                    ReviewedAt = r.ReviewedAt,

                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(qNorm))
            {
                rows = rows.Where(x =>
                    (x.InstitutionName ?? "").ToLowerInvariant().Contains(qNorm) ||
                    (x.ContentProductName ?? "").ToLowerInvariant().Contains(qNorm) ||
                    (x.RequestedByUsername ?? "").ToLowerInvariant().Contains(qNorm) ||
                    (x.RequestNotes ?? "").ToLowerInvariant().Contains(qNorm) ||
                    x.Id.ToString().Contains(qNorm) ||
                    x.SubscriptionId.ToString().Contains(qNorm)
                ).ToList();
            }

            return Ok(rows);
        }

        [Authorize(Policy = PolicyNames.IsGlobalAdmin)]
        [HttpGet("requests/{requestId:int}")]
        public async Task<IActionResult> GetRequestById([FromRoute] int requestId)
        {
            var users = _db.Users.AsNoTracking();

            var row = await _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.Institution)
                .Include(r => r.Subscription)
                    .ThenInclude(s => s.ContentProduct)
                .Where(r => r.Id == requestId)
                .Select(r => new SubscriptionActionRequestListDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,

                    InstitutionId = r.Subscription.InstitutionId,
                    InstitutionName = r.Subscription.Institution.Name,

                    ContentProductId = r.Subscription.ContentProductId,
                    ContentProductName = r.Subscription.ContentProduct.Name,

                    RequestType = r.RequestType,
                    Status = r.Status,

                    RequestedByUserId = r.RequestedByUserId,
                    RequestedByUsername = users
                        .Where(u => u.Id == r.RequestedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault() ?? "",

                    RequestNotes = r.RequestNotes,

                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUserId.HasValue
                        ? (users.Where(u => u.Id == r.ReviewedByUserId.Value).Select(u => u.Username).FirstOrDefault() ?? "")
                        : null,

                    ReviewNotes = r.ReviewNotes,
                    ReviewedAt = r.ReviewedAt,

                    CreatedAt = r.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (row == null) return NotFound(new { message = "Request not found." });
            return Ok(row);
        }

        //Add
        // ✅ List MY requests (Admin can see what they submitted; pending by default)
        [HttpGet("requests/mine")]
        public async Task<IActionResult> GetMyRequests(
            [FromQuery] string? status = "pending",
            [FromQuery] string? q = null)
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Unauthorized();

            var statusNorm = (status ?? "pending").Trim().ToLowerInvariant();
            var qNorm = (q ?? "").Trim().ToLowerInvariant();

            var query = _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Include(r => r.Subscription).ThenInclude(s => s.Institution)
                .Include(r => r.Subscription).ThenInclude(s => s.ContentProduct)
                .Where(r => r.RequestedByUserId == userId.Value)
                .OrderByDescending(r => r.Id)
                .AsQueryable();

            if (statusNorm != "all")
            {
                query = statusNorm switch
                {
                    "pending" => query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending),
                    "approved" => query.Where(r => r.Status == SubscriptionActionRequestStatus.Approved),
                    "rejected" => query.Where(r => r.Status == SubscriptionActionRequestStatus.Rejected),
                    _ => query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending),
                };
            }

            var users = _db.Users.AsNoTracking();

            var rows = await query
                .Select(r => new SubscriptionActionRequestListDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,

                    InstitutionId = r.Subscription.InstitutionId,
                    InstitutionName = r.Subscription.Institution.Name,

                    ContentProductId = r.Subscription.ContentProductId,
                    ContentProductName = r.Subscription.ContentProduct.Name,

                    RequestType = r.RequestType,
                    Status = r.Status,

                    RequestedByUserId = r.RequestedByUserId,
                    RequestedByUsername = users
                        .Where(u => u.Id == r.RequestedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault() ?? "",

                    RequestNotes = r.RequestNotes,

                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUserId.HasValue
                        ? (users.Where(u => u.Id == r.ReviewedByUserId.Value).Select(u => u.Username).FirstOrDefault() ?? "")
                        : null,

                    ReviewNotes = r.ReviewNotes,
                    ReviewedAt = r.ReviewedAt,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(qNorm))
            {
                rows = rows.Where(x =>
                    (x.InstitutionName ?? "").ToLowerInvariant().Contains(qNorm) ||
                    (x.ContentProductName ?? "").ToLowerInvariant().Contains(qNorm) ||
                    (x.RequestNotes ?? "").ToLowerInvariant().Contains(qNorm) ||
                    x.Id.ToString().Contains(qNorm) ||
                    x.SubscriptionId.ToString().Contains(qNorm)
                ).ToList();
            }

            return Ok(rows);
        }

        // ✅ Admin can view THEIR OWN requests (pending by default)
        // GET: /api/institutions/subscriptions/requests/my?status=pending|approved|rejected|all
        [HttpGet("requests/my")]
        public async Task<IActionResult> GetMyRequests([FromQuery] string? status = "pending")
        {
            var userId = GetUserId();
            if (!userId.HasValue) return Unauthorized();

            var statusNorm = (status ?? "pending").Trim().ToLowerInvariant();

            var query = _db.InstitutionSubscriptionActionRequests
                .AsNoTracking()
                .Include(r => r.Subscription).ThenInclude(s => s.Institution)
                .Include(r => r.Subscription).ThenInclude(s => s.ContentProduct)
                .Where(r => r.RequestedByUserId == userId.Value)
                .OrderByDescending(r => r.Id)
                .AsQueryable();

            if (statusNorm != "all")
            {
                if (statusNorm == "pending")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending);
                else if (statusNorm == "approved")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Approved);
                else if (statusNorm == "rejected")
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Rejected);
                else
                    query = query.Where(r => r.Status == SubscriptionActionRequestStatus.Pending);
            }

            var users = _db.Users.AsNoTracking();

            var rows = await query
                .Select(r => new SubscriptionActionRequestListDto
                {
                    Id = r.Id,
                    SubscriptionId = r.SubscriptionId,

                    InstitutionId = r.Subscription.InstitutionId,
                    InstitutionName = r.Subscription.Institution.Name,

                    ContentProductId = r.Subscription.ContentProductId,
                    ContentProductName = r.Subscription.ContentProduct.Name,

                    RequestType = r.RequestType,
                    Status = r.Status,

                    RequestedByUserId = r.RequestedByUserId,
                    RequestedByUsername = users
                        .Where(u => u.Id == r.RequestedByUserId)
                        .Select(u => u.Username)
                        .FirstOrDefault() ?? "",

                    RequestNotes = r.RequestNotes,

                    ReviewedByUserId = r.ReviewedByUserId,
                    ReviewedByUsername = r.ReviewedByUserId.HasValue
                        ? (users.Where(u => u.Id == r.ReviewedByUserId.Value).Select(u => u.Username).FirstOrDefault() ?? "")
                        : null,

                    ReviewNotes = r.ReviewNotes,
                    ReviewedAt = r.ReviewedAt,

                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            return Ok(rows);
        }


    }
}
