using System;
using System.Linq;
using System.Threading.Tasks;
using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs.AdminDashboard;
using LawAfrica.API.Models.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/payments")]
    [Authorize(Roles = "Admin")]
    public class AdminPaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminPaymentsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminPaymentRowDto>>> List([FromQuery] AdminPaymentsQuery query)
        {
            var now = DateTime.UtcNow;

            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize is < 1 ? 20 : (query.PageSize > 200 ? 200 : query.PageSize);

            var q = (query.Q ?? "").Trim();

            var fromUtc = query.FromUtc?.ToUniversalTime();
            var toUtc = query.ToUtc?.ToUniversalTime();

            var paymentsQ = _db.PaymentIntents.AsNoTracking();

            // date range filter (CreatedAt is UTC)
            if (fromUtc.HasValue)
                paymentsQ = paymentsQ.Where(p => p.CreatedAt >= fromUtc.Value);
            if (toUtc.HasValue)
                paymentsQ = paymentsQ.Where(p => p.CreatedAt < toUtc.Value);

            if (query.InstitutionId.HasValue)
                paymentsQ = paymentsQ.Where(p => p.InstitutionId == query.InstitutionId.Value);

            if (query.UserId.HasValue)
                paymentsQ = paymentsQ.Where(p => p.UserId == query.UserId.Value);

            if (!string.IsNullOrWhiteSpace(query.Currency))
                paymentsQ = paymentsQ.Where(p => p.Currency == query.Currency);

            if (query.MinAmount.HasValue)
                paymentsQ = paymentsQ.Where(p => p.Amount >= query.MinAmount.Value);

            if (query.MaxAmount.HasValue)
                paymentsQ = paymentsQ.Where(p => p.Amount <= query.MaxAmount.Value);

            // payer type filter
            var payerType = (query.PayerType ?? "").Trim().ToLowerInvariant();
            if (payerType == "institution")
                paymentsQ = paymentsQ.Where(p => p.InstitutionId != null);
            else if (payerType == "individual")
                paymentsQ = paymentsQ.Where(p => p.InstitutionId == null);

            // status/purpose/method/provider filters from string -> enum parsing
            if (!string.IsNullOrWhiteSpace(query.Status) &&
                Enum.TryParse<PaymentStatus>(query.Status.Trim(), ignoreCase: true, out var status))
            {
                paymentsQ = paymentsQ.Where(p => p.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query.Purpose) &&
                Enum.TryParse<PaymentPurpose>(query.Purpose.Trim(), ignoreCase: true, out var purpose))
            {
                paymentsQ = paymentsQ.Where(p => p.Purpose == purpose);
            }

            if (!string.IsNullOrWhiteSpace(query.Method) &&
                Enum.TryParse<PaymentMethod>(query.Method.Trim(), ignoreCase: true, out var method))
            {
                paymentsQ = paymentsQ.Where(p => p.Method == method);
            }

            if (!string.IsNullOrWhiteSpace(query.Provider) &&
                Enum.TryParse<PaymentProvider>(query.Provider.Trim(), ignoreCase: true, out var provider))
            {
                paymentsQ = paymentsQ.Where(p => p.Provider == provider);
            }

            // Join to get InstitutionName + UserEmail (safe left joins)
            var joined =
                from p in paymentsQ
                join inst in _db.Institutions.AsNoTracking() on p.InstitutionId equals inst.Id into instJoin
                from inst in instJoin.DefaultIfEmpty()
                join u in _db.Users.AsNoTracking() on p.UserId equals u.Id into userJoin
                from u in userJoin.DefaultIfEmpty()
                select new
                {
                    p.Id,
                    p.CreatedAt,
                    p.Status,
                    p.Purpose,
                    p.Method,
                    p.Provider,
                    p.Amount,
                    p.Currency,
                    p.InstitutionId,
                    InstitutionName = inst != null ? inst.Name : null,
                    p.UserId,
                    UserEmail = u != null ? u.Email : null,
                    p.MpesaReceiptNumber,
                    p.ManualReference
                };

            // free-text search
            if (!string.IsNullOrWhiteSpace(q))
            {
                joined = joined.Where(x =>
                    (x.InstitutionName != null && x.InstitutionName.Contains(q)) ||
                    (x.UserEmail != null && x.UserEmail.Contains(q)) ||
                    (x.MpesaReceiptNumber != null && x.MpesaReceiptNumber.Contains(q)) ||
                    (x.ManualReference != null && x.ManualReference.Contains(q)) ||
                    x.Currency.Contains(q) ||
                    x.Purpose.ToString().Contains(q) ||
                    x.Status.ToString().Contains(q)
                );
            }

            // sorting
            joined = (query.Sort ?? "").ToLowerInvariant() switch
            {
                "createdat_asc" => joined.OrderBy(x => x.CreatedAt),
                "createdat_desc" => joined.OrderByDescending(x => x.CreatedAt),

                "amount_asc" => joined.OrderBy(x => x.Amount),
                "amount_desc" => joined.OrderByDescending(x => x.Amount),

                "status_asc" => joined.OrderBy(x => x.Status),
                "status_desc" => joined.OrderByDescending(x => x.Status),

                _ => joined.OrderByDescending(x => x.CreatedAt)
            };

            var total = await joined.CountAsync();

            var items = await joined
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new AdminPaymentRowDto(
                    PaymentIntentId: x.Id,
                    CreatedAtUtc: DateTime.SpecifyKind(x.CreatedAt, DateTimeKind.Utc),

                    Status: x.Status.ToString(),
                    Purpose: x.Purpose.ToString(),
                    Method: x.Method.ToString(),
                    Provider: x.Provider.ToString(),

                    Amount: x.Amount,
                    Currency: x.Currency,

                    PayerType: x.InstitutionId != null ? "Institution" : "Individual",
                    InstitutionId: x.InstitutionId,
                    InstitutionName: x.InstitutionName,
                    UserId: x.UserId,
                    UserEmail: x.UserEmail,

                    MpesaReceiptNumber: x.MpesaReceiptNumber,
                    ManualReference: x.ManualReference
                ))
                .ToListAsync();

            return Ok(new PagedResult<AdminPaymentRowDto>(items, page, pageSize, total));
        }
    }
}
