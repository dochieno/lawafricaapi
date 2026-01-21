using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
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

        // =========================
        // Helpers
        // =========================
        private static DateTime? ToUtc(DateTime? dt)
        {
            if (!dt.HasValue) return null;
            var v = dt.Value;
            return v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime();
        }

        public class PagedResultDto<T>
        {
            public List<T> Items { get; set; } = new();
            public int Page { get; set; }
            public int PageSize { get; set; }
            public int TotalCount { get; set; }
        }

        // ==========================================================
        // ✅ EXISTING ENDPOINT (KEEP AS-IS)
        // GET /api/admin/payments
        // ==========================================================
        [HttpGet]
        public async Task<ActionResult<PagedResult<AdminPaymentRowDto>>> List(
            [FromQuery] AdminPaymentsQuery query,
            CancellationToken ct = default)
        {
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

            var total = await joined.CountAsync(ct);

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
                .ToListAsync(ct);

            return Ok(new PagedResult<AdminPaymentRowDto>(items, page, pageSize, total));
        }

        // ==========================================================
        // ✅ NEW ENDPOINTS — matches AdminPayments.jsx (Finance tab)
        // ==========================================================

        // --------------------------
        // 1) Intents
        // GET /api/admin/payments/intents?q=&provider=&from=&to=&page=&pageSize=
        // --------------------------
        public class PaymentIntentListItemDto
        {
            public int Id { get; set; }
            public string Provider { get; set; } = "";
            public string? ProviderReference { get; set; }
            public string? ProviderTransactionId { get; set; }

            public string Purpose { get; set; } = "";
            public string Currency { get; set; } = "KES";
            public decimal Amount { get; set; }

            public string Status { get; set; } = "";
            public DateTime CreatedAt { get; set; }

            // ✅ your model does NOT have PaidAt; use ProviderPaidAt/ApprovedAt
            public DateTime? PaidAt { get; set; }

            public int? InvoiceId { get; set; }
            public int? UserId { get; set; }
            public int? InstitutionId { get; set; }
        }

        [HttpGet("intents")]
        public async Task<ActionResult<PagedResultDto<PaymentIntentListItemDto>>> ListIntents(
            [FromQuery] string? q,
            [FromQuery] string? provider,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 5 or > 200 ? 25 : pageSize;

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);
            var s = (q ?? "").Trim();
            var prov = (provider ?? "").Trim();

            var query = _db.PaymentIntents.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(prov) &&
                Enum.TryParse<PaymentProvider>(prov, ignoreCase: true, out var pEnum))
            {
                query = query.Where(x => x.Provider == pEnum);
            }

            if (fromUtc.HasValue) query = query.Where(x => x.CreatedAt >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(x => x.CreatedAt <= toUtc.Value);

            if (!string.IsNullOrWhiteSpace(s))
            {
                query = query.Where(x =>
                    (x.ProviderReference != null && x.ProviderReference.Contains(s)) ||
                    (x.ProviderTransactionId != null && x.ProviderTransactionId.Contains(s)) ||
                    (x.MpesaReceiptNumber != null && x.MpesaReceiptNumber.Contains(s)) ||
                    (x.ManualReference != null && x.ManualReference.Contains(s)) ||
                    x.Purpose.ToString().Contains(s) ||
                    x.Status.ToString().Contains(s)
                );
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(x => x.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new PaymentIntentListItemDto
                {
                    Id = x.Id,
                    Provider = x.Provider.ToString(),
                    ProviderReference = x.ProviderReference,
                    ProviderTransactionId = x.ProviderTransactionId,

                    Purpose = x.Purpose.ToString(),
                    Currency = x.Currency,
                    Amount = x.Amount,

                    Status = x.Status.ToString(),
                    CreatedAt = x.CreatedAt,

                    // ✅ map to ProviderPaidAt first; fallback to ApprovedAt
                    PaidAt = x.ProviderPaidAt ?? x.ApprovedAt,

                    InvoiceId = x.InvoiceId,
                    UserId = x.UserId,
                    InstitutionId = x.InstitutionId
                })
                .ToListAsync(ct);

            return Ok(new PagedResultDto<PaymentIntentListItemDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            });
        }

        // --------------------------
        // 2) Provider Transactions
        // GET /api/admin/payments/transactions?q=&provider=&from=&to=&page=&pageSize=
        // --------------------------
        public class PaymentTxnListItemDto
        {
            public long Id { get; set; }
            public string Provider { get; set; } = "";
            public string ProviderTransactionId { get; set; } = "";
            public string? Reference { get; set; }

            public string Currency { get; set; } = "KES";
            public decimal Amount { get; set; }

            public string? Channel { get; set; }
            public DateTime? PaidAt { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        [HttpGet("transactions")]
        public async Task<ActionResult<PagedResultDto<PaymentTxnListItemDto>>> ListTransactions(
            [FromQuery] string? q,
            [FromQuery] string? provider,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 5 or > 200 ? 25 : pageSize;

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);
            var s = (q ?? "").Trim();
            var prov = (provider ?? "").Trim();

            var query = _db.PaymentProviderTransactions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(prov) &&
                Enum.TryParse<PaymentProvider>(prov, ignoreCase: true, out var pEnum))
            {
                query = query.Where(x => x.Provider == pEnum);
            }

            // Your model uses CreatedAt on PaymentProviderTransaction (per your DbContext indexes)
            if (fromUtc.HasValue) query = query.Where(x => x.LastSeenAt >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(x => x.LastSeenAt <= toUtc.Value);

            if (!string.IsNullOrWhiteSpace(s))
            {
                query = query.Where(x =>
                    x.ProviderTransactionId.Contains(s) ||
                    (x.Reference != null && x.Reference.Contains(s))
                );
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(x => x.LastSeenAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new PaymentTxnListItemDto
                {
                    Id = x.Id,
                    Provider = x.Provider.ToString(),
                    ProviderTransactionId = x.ProviderTransactionId,
                    Reference = x.Reference,
                    Currency = x.Currency,
                    Amount = x.Amount,
                    Channel = x.Channel,
                    PaidAt = x.PaidAt,
                    CreatedAt = x.LastSeenAt,
                })
                .ToListAsync(ct);

            return Ok(new PagedResultDto<PaymentTxnListItemDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            });
        }

        // --------------------------
        // 3) Webhook Events
        // GET /api/admin/payments/webhooks?q=&provider=&from=&to=&page=&pageSize=
        // --------------------------
        public class WebhookEventListItemDto
        {
            public long Id { get; set; } // ✅ your model uses long
            public string Provider { get; set; } = "";
            public string EventType { get; set; } = "";
            public string? ProviderEventId { get; set; }
            public string? Reference { get; set; }

            public DateTime ReceivedAt { get; set; }

            // ✅ you don't have Processed bool; derive it
            public bool Processed { get; set; }

            public string? ProcessingError { get; set; }

            // optional: expose status for debugging
            public string ProcessingStatus { get; set; } = "";
        }

        [HttpGet("webhooks")]
        public async Task<ActionResult<PagedResultDto<WebhookEventListItemDto>>> ListWebhooks(
            [FromQuery] string? q,
            [FromQuery] string? provider,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize is < 5 or > 200 ? 25 : pageSize;

            var fromUtc = ToUtc(from);
            var toUtc = ToUtc(to);
            var s = (q ?? "").Trim();
            var prov = (provider ?? "").Trim();

            var query = _db.PaymentProviderWebhookEvents.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(prov) &&
                Enum.TryParse<PaymentProvider>(prov, ignoreCase: true, out var pEnum))
            {
                query = query.Where(x => x.Provider == pEnum);
            }

            if (fromUtc.HasValue) query = query.Where(x => x.ReceivedAt >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(x => x.ReceivedAt <= toUtc.Value);

            if (!string.IsNullOrWhiteSpace(s))
            {
                query = query.Where(x =>
                    (x.Reference != null && x.Reference.Contains(s)) ||
                    (x.ProviderEventId != null && x.ProviderEventId.Contains(s)) ||
                    (x.EventType != null && x.EventType.Contains(s))
                );
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(x => x.ReceivedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new WebhookEventListItemDto
                {
                    Id = x.Id,
                    Provider = x.Provider.ToString(),
                    EventType = x.EventType,
                    ProviderEventId = x.ProviderEventId,
                    Reference = x.Reference,
                    ReceivedAt = x.ReceivedAt,
                    Processed = x.ProcessedAt != null || x.ProcessingStatus != ProviderEventProcessingStatus.Received,
                    ProcessingError = x.ProcessingError,
                    ProcessingStatus = x.ProcessingStatus.ToString()
                })
                .ToListAsync(ct);

            return Ok(new PagedResultDto<WebhookEventListItemDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalCount = total
            });
        }
    }
}
