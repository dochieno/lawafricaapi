using System.Security.Claims;
using LawAfrica.API.Data;
using LawAfrica.API.DTOs.Admin.Trials;
using LawAfrica.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/trials")]
    [Authorize(Roles = "Admin")] // adjust if you use permission-based checks
    public class GlobalAdminTrialsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public GlobalAdminTrialsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ---------------------------
        // POST: /api/admin/trials/grant
        // Grants a trial or extends it (based on ExtendIfActive)
        // ---------------------------
        [HttpPost("grant")]
        public async Task<ActionResult<TrialSubscriptionResultDto>> GrantTrial([FromBody] GrantTrialRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var adminId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            // Validate user & product exist (nice errors)
            var userExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == req.UserId, ct);
            if (!userExists) return NotFound(new { message = "User not found." });

            var productExists = await _db.ContentProducts.AsNoTracking().AnyAsync(x => x.Id == req.ContentProductId, ct);
            if (!productExists) return NotFound(new { message = "Content product not found." });

            using var tx = await _db.Database.BeginTransactionAsync(ct);

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(x => x.UserId == req.UserId && x.ContentProductId == req.ContentProductId, ct);

            if (sub == null)
            {
                sub = new UserProductSubscription
                {
                    UserId = req.UserId,
                    ContentProductId = req.ContentProductId,
                    Status = SubscriptionStatus.Active,
                    StartDate = now,
                    EndDate = AddDuration(now, req.Unit, req.Value),
                    IsTrial = true,
                    GrantedByUserId = adminId
                };

                _db.UserProductSubscriptions.Add(sub);
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                return Ok(ToResult(sub, action: "GrantedTrial"));
            }

            // If exists:
            // - If currently active (within window) and ExtendIfActive=true => extend EndDate
            // - Else reset from now
            var isActiveNow = sub.Status == SubscriptionStatus.Active && sub.StartDate <= now && sub.EndDate >= now;

            sub.IsTrial = true;
            sub.GrantedByUserId = adminId;
            sub.Status = SubscriptionStatus.Active;

            if (isActiveNow && req.ExtendIfActive)
            {
                sub.EndDate = AddDuration(sub.EndDate, req.Unit, req.Value); // extend from current EndDate
            }
            else
            {
                sub.StartDate = now;
                sub.EndDate = AddDuration(now, req.Unit, req.Value);
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return Ok(ToResult(sub, action: isActiveNow && req.ExtendIfActive ? "ExtendedTrial" : "GrantedTrialReset"));
        }

        // ---------------------------
        // POST: /api/admin/trials/extend
        // Extends existing trial; if expired, extends from now.
        // ---------------------------
        [HttpPost("extend")]
        public async Task<ActionResult<TrialSubscriptionResultDto>> ExtendTrial([FromBody] ExtendTrialRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var adminId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(x => x.UserId == req.UserId && x.ContentProductId == req.ContentProductId, ct);

            if (sub == null)
                return NotFound(new { message = "No subscription exists for this user and product. Use /grant first." });

            // You said trial is per user and extendable
            sub.IsTrial = true;
            sub.GrantedByUserId = adminId;
            sub.Status = SubscriptionStatus.Active;

            // Extend from the later of (now, current EndDate)
            var baseTime = sub.EndDate >= now ? sub.EndDate : now;

            // If it was expired, also reset StartDate to now to reflect “trial resumed”
            if (sub.EndDate < now)
                sub.StartDate = now;

            sub.EndDate = AddDuration(baseTime, req.Unit, req.Value);

            await _db.SaveChangesAsync(ct);

            return Ok(ToResult(sub, action: "ExtendedTrial"));
        }

        // ---------------------------
        // POST: /api/admin/trials/revoke
        // Ends trial immediately.
        // ---------------------------
        [HttpPost("revoke")]
        public async Task<ActionResult<TrialSubscriptionResultDto>> RevokeTrial([FromBody] RevokeTrialRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var adminId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(x => x.UserId == req.UserId && x.ContentProductId == req.ContentProductId, ct);

            if (sub == null)
                return NotFound(new { message = "Subscription not found." });

            // We avoid guessing extra enum values. Ending by EndDate guarantees entitlement fails.
            sub.IsTrial = false; // optional: mark it not a trial anymore (or keep true; your choice)
            sub.GrantedByUserId = adminId;
            sub.EndDate = now.AddSeconds(-1);

            await _db.SaveChangesAsync(ct);

            return Ok(ToResult(sub, action: "RevokedTrial"));
        }

        // ---------------------------
        // OPTIONAL: Grant paid subscription (1/6/12 months)
        // POST: /api/admin/trials/grant-paid
        // ---------------------------
        [HttpPost("grant-paid")]
        public async Task<ActionResult<TrialSubscriptionResultDto>> GrantPaid([FromBody] GrantPaidSubscriptionRequest req, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var adminId = GetCurrentUserId();
            var now = DateTime.UtcNow;

            var userExists = await _db.Users.AsNoTracking().AnyAsync(x => x.Id == req.UserId, ct);
            if (!userExists) return NotFound(new { message = "User not found." });

            var productExists = await _db.ContentProducts.AsNoTracking().AnyAsync(x => x.Id == req.ContentProductId, ct);
            if (!productExists) return NotFound(new { message = "Content product not found." });

            var sub = await _db.UserProductSubscriptions
                .FirstOrDefaultAsync(x => x.UserId == req.UserId && x.ContentProductId == req.ContentProductId, ct);

            if (sub == null)
            {
                sub = new UserProductSubscription
                {
                    UserId = req.UserId,
                    ContentProductId = req.ContentProductId,
                    Status = SubscriptionStatus.Active,
                    StartDate = now,
                    EndDate = now.AddMonths(req.Months),
                    IsTrial = false,
                    GrantedByUserId = adminId
                };
                _db.UserProductSubscriptions.Add(sub);
            }
            else
            {
                sub.Status = SubscriptionStatus.Active;
                sub.IsTrial = false;
                sub.GrantedByUserId = adminId;

                // If still active, extend from EndDate; else start new from now
                if (sub.EndDate >= now && sub.StartDate <= now)
                    sub.EndDate = sub.EndDate.AddMonths(req.Months);
                else
                {
                    sub.StartDate = now;
                    sub.EndDate = now.AddMonths(req.Months);
                }
            }

            await _db.SaveChangesAsync(ct);

            return Ok(ToResult(sub, action: "GrantedPaidSubscription"));
        }

        // ---------------------------
        // Helpers
        // ---------------------------

        private int GetCurrentUserId()
        {
            // Support common claim types
            var raw =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                User.FindFirstValue("sub") ??
                User.FindFirstValue("id");

            if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw, out var id))
                throw new InvalidOperationException("Cannot determine current user id from claims.");

            return id;
        }

        private static DateTime AddDuration(DateTime start, DurationUnit unit, int value)
        {
            return unit switch
            {
                DurationUnit.Days => start.AddDays(value),
                DurationUnit.Months => start.AddMonths(value),
                _ => start.AddDays(value)
            };
        }

        private static TrialSubscriptionResultDto ToResult(UserProductSubscription sub, string action)
        {
            return new TrialSubscriptionResultDto
            {
                SubscriptionId = sub.Id,
                UserId = sub.UserId,
                ContentProductId = sub.ContentProductId,
                IsTrial = sub.IsTrial,
                Status = sub.Status.ToString(),
                StartDate = sub.StartDate,
                EndDate = sub.EndDate,
                GrantedByUserId = sub.GrantedByUserId,
                Action = action
            };
        }
    }
}
