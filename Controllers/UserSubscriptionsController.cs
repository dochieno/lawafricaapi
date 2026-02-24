// =======================================================
// FILE: Controllers/UserSubscriptionsController.cs
// =======================================================
using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Subscriptions;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/subscriptions")]
    public class UserSubscriptionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        // ✅ Reuse existing payment plumbing (no duplicate payment controllers/services)
        private readonly MpesaService _mpesa;
        private readonly PaystackService _paystack;
        private readonly PaystackOptions _paystackOpts;
        private readonly PaymentValidationService _paymentValidation;

        public UserSubscriptionsController(
            ApplicationDbContext db,
            MpesaService mpesa,
            PaystackService paystack,
            IOptions<PaystackOptions> paystackOpts,
            PaymentValidationService paymentValidation)
        {
            _db = db;
            _mpesa = mpesa;
            _paystack = paystack;
            _paystackOpts = paystackOpts.Value;
            _paymentValidation = paymentValidation;
        }

        // =========================
        // USER: My subscriptions
        // GET /api/subscriptions/me
        // =========================
        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<MySubscriptionsResponseDto>> MySubscriptions(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var rows = await _db.UserProductSubscriptions
                .AsNoTracking()
                .Include(s => s.ContentProduct)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.EndDate)
                .Select(s => new
                {
                    s.Id,
                    s.ContentProductId,
                    ProductName = s.ContentProduct.Name,
                    s.Status,
                    s.IsTrial,
                    s.StartDate,
                    s.EndDate
                })
                .ToListAsync(ct);

            var items = rows.Select(s =>
            {
                var isActiveNow =
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now &&
                    s.EndDate >= now;

                var daysRemaining = s.EndDate > now
                    ? (int)Math.Ceiling((s.EndDate - now).TotalDays)
                    : 0;

                return new MySubscriptionItemDto
                {
                    Id = s.Id,
                    ContentProductId = s.ContentProductId,
                    ProductName = s.ProductName,
                    Status = s.Status,
                    IsTrial = s.IsTrial,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    IsActiveNow = isActiveNow,
                    DaysRemaining = daysRemaining
                };
            }).ToList();

            return Ok(new MySubscriptionsResponseDto
            {
                UserId = userId,
                NowUtc = now,
                Items = items
            });
        }

        // ==========================================
        // USER: available subscription products/plans
        // GET /api/subscriptions/products
        // ==========================================
        [Authorize]
        [HttpGet("products")]
        public async Task<ActionResult<SubscriptionProductsResponseDto>> Products(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new { x.UserType, x.InstitutionId })
                .FirstOrDefaultAsync(ct);

            if (u == null) return Unauthorized();

            var isPublicIndividual = u.UserType == UserType.Public && u.InstitutionId == null;
            var audience = isPublicIndividual ? PricingAudience.Public : PricingAudience.Institution;

            // Only subscription products for this audience
            var products = await _db.ContentProducts
                .AsNoTracking()
                .Where(p =>
                    (audience == PricingAudience.Public ? p.AvailableToPublic : p.AvailableToInstitutions) &&
                    ((audience == PricingAudience.Public ? p.PublicAccessModel : p.InstitutionAccessModel) == ProductAccessModel.Subscription)
                )
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.Description,
                    AccessModel = (audience == PricingAudience.Public ? p.PublicAccessModel : p.InstitutionAccessModel)
                })
                .ToListAsync(ct);

            var productIds = products.Select(x => x.Id).ToList();

            // Plans filtered by audience + active + effective window
            var plans = await _db.ContentProductPrices
                .AsNoTracking()
                .Where(pp =>
                    productIds.Contains(pp.ContentProductId) &&
                    pp.Audience == audience &&
                    pp.IsActive &&
                    (!pp.EffectiveFromUtc.HasValue || pp.EffectiveFromUtc.Value <= now) &&
                    (!pp.EffectiveToUtc.HasValue || pp.EffectiveToUtc.Value >= now)
                )
                .OrderBy(pp => pp.ContentProductId)
                .ThenBy(pp => pp.BillingPeriod)
                .Select(pp => new
                {
                    pp.Id,
                    pp.ContentProductId,
                    pp.BillingPeriod,
                    pp.Currency,
                    pp.Amount,
                    pp.IsActive,
                    pp.EffectiveFromUtc,
                    pp.EffectiveToUtc
                })
                .ToListAsync(ct);

            var dto = new SubscriptionProductsResponseDto
            {
                NowUtc = now,
                Audience = audience,
                Products = products.Select(p => new SubscriptionProductDto
                {
                    ContentProductId = p.Id,
                    Name = p.Name,
                    Description = p.Description,
                    AccessModel = p.AccessModel,
                    Plans = plans
                        .Where(x => x.ContentProductId == p.Id)
                        .Select(x => new SubscriptionPlanDto
                        {
                            ContentProductPriceId = x.Id,
                            BillingPeriod = x.BillingPeriod,
                            Currency = string.IsNullOrWhiteSpace(x.Currency) ? "KES" : x.Currency.Trim().ToUpperInvariant(),
                            Amount = x.Amount,
                            IsActive = x.IsActive,
                            EffectiveFromUtc = x.EffectiveFromUtc,
                            EffectiveToUtc = x.EffectiveToUtc
                        })
                        .ToList()
                }).ToList()
            };

            return Ok(dto);
        }

        // ==========================================
        // USER: create checkout for selected plan
        // POST /api/subscriptions/checkout
        // ==========================================
        [Authorize]
        [HttpPost("checkout")]
        public async Task<ActionResult<CreateSubscriptionCheckoutResponseDto>> Checkout(
            [FromBody] CreateSubscriptionCheckoutRequestDto req,
            CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            var u = await _db.Users
                .AsNoTracking()
                .Where(x => x.Id == userId)
                .Select(x => new { x.UserType, x.InstitutionId })
                .FirstOrDefaultAsync(ct);

            if (u == null) return Unauthorized();

            var isPublicIndividual = u.UserType == UserType.Public && u.InstitutionId == null;
            var audience = isPublicIndividual ? PricingAudience.Public : PricingAudience.Institution;

            // Load plan (server truth)
            var plan = await _db.ContentProductPrices
                .AsNoTracking()
                .Where(p => p.Id == req.ContentProductPriceId)
                .Select(p => new
                {
                    p.Id,
                    p.ContentProductId,
                    p.Audience,
                    p.BillingPeriod,
                    p.Amount,
                    p.Currency,
                    p.IsActive,
                    p.EffectiveFromUtc,
                    p.EffectiveToUtc
                })
                .FirstOrDefaultAsync(ct);

            if (plan == null)
                return BadRequest(new { message = "Pricing plan not found." });

            if (plan.Audience != audience)
                return BadRequest(new { message = "Pricing plan is not available for this account type." });

            if (!plan.IsActive)
                return BadRequest(new { message = "Pricing plan is inactive." });

            if (plan.EffectiveFromUtc.HasValue && plan.EffectiveFromUtc.Value > now)
                return BadRequest(new { message = "Pricing plan is not yet effective." });

            if (plan.EffectiveToUtc.HasValue && plan.EffectiveToUtc.Value < now)
                return BadRequest(new { message = "Pricing plan has expired." });

            // Load product + validate access model
            var product = await _db.ContentProducts
                .AsNoTracking()
                .Where(p => p.Id == plan.ContentProductId)
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    p.PublicAccessModel,
                    p.InstitutionAccessModel
                })
                .FirstOrDefaultAsync(ct);

            if (product == null)
                return BadRequest(new { message = "Content product not found." });

            var productAccessModel = isPublicIndividual ? product.PublicAccessModel : product.InstitutionAccessModel;
            if (productAccessModel != ProductAccessModel.Subscription)
                return BadRequest(new { message = "This product is not subscription-based." });

            var purpose = isPublicIndividual
                ? PaymentPurpose.PublicProductSubscription
                : PaymentPurpose.InstitutionProductSubscription;

            // Legacy DurationInMonths on PaymentIntent (still used downstream)
            var durationMonths = plan.BillingPeriod == BillingPeriod.Monthly ? 1 : 12;

            var amount = plan.Amount;
            var currency = string.IsNullOrWhiteSpace(plan.Currency) ? "KES" : plan.Currency.Trim().ToUpperInvariant();

            // ----------------------------
            // Mpesa
            // ----------------------------
            if (req.Provider == PaymentProvider.Mpesa)
            {
                if (string.IsNullOrWhiteSpace(req.PhoneNumber))
                    return BadRequest(new { message = "PhoneNumber is required for Mpesa." });

                await _paymentValidation.ValidateStkInitiateAsync(
                    purpose: purpose,
                    amount: amount,
                    phoneNumber: req.PhoneNumber,
                    registrationIntentId: null,
                    contentProductId: product.Id,
                    institutionId: u.InstitutionId,
                    durationInMonths: durationMonths,
                    legalDocumentId: null,
                    contentProductPriceId: plan.Id
                );

                var intent = new PaymentIntent
                {
                    Provider = PaymentProvider.Mpesa,
                    Method = PaymentMethod.Mpesa,
                    Purpose = purpose,
                    Status = PaymentStatus.Pending,

                    UserId = userId,
                    InstitutionId = u.InstitutionId,
                    ContentProductId = product.Id,
                    ContentProductPriceId = plan.Id,

                    DurationInMonths = durationMonths,
                    Amount = amount,
                    Currency = currency,

                    PhoneNumber = req.PhoneNumber.Trim(),
                    ClientReturnUrl = string.IsNullOrWhiteSpace(req.ClientReturnUrl) ? null : req.ClientReturnUrl.Trim()
                };

                _db.PaymentIntents.Add(intent);
                await _db.SaveChangesAsync(ct);

                var token = await _mpesa.GetAccessTokenAsync();
                var (merchantRequestId, checkoutRequestId, _) = await _mpesa.InitiateStkPushAsync(
                    token,
                    intent.PhoneNumber!,
                    amount,
                    accountReference: $"LA-{intent.Id}",
                    transactionDesc: $"{purpose}"
                );

                intent.MerchantRequestId = merchantRequestId;
                intent.CheckoutRequestId = checkoutRequestId;
                intent.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);

                return Ok(new CreateSubscriptionCheckoutResponseDto
                {
                    PaymentIntentId = intent.Id,
                    Provider = intent.Provider,
                    Status = intent.Status.ToString(),
                    Amount = intent.Amount,
                    Currency = intent.Currency,
                    MerchantRequestId = merchantRequestId,
                    CheckoutRequestId = checkoutRequestId,
                    Message = "STK Push sent. Please check your phone to complete payment."
                });
            }

            // ----------------------------
            // Paystack
            // ----------------------------
            if (req.Provider == PaymentProvider.Paystack)
            {
                var email = await _db.Users
                    .AsNoTracking()
                    .Where(x => x.Id == userId)
                    .Select(x => x.Email)
                    .FirstOrDefaultAsync(ct);

                if (string.IsNullOrWhiteSpace(email))
                    return BadRequest(new { message = "Your account has no email. Paystack requires an email." });

                var intent = new PaymentIntent
                {
                    Provider = PaymentProvider.Paystack,
                    Method = PaymentMethod.Paystack,
                    Purpose = purpose,
                    Status = PaymentStatus.Pending,

                    UserId = userId,
                    InstitutionId = u.InstitutionId,
                    ContentProductId = product.Id,
                    ContentProductPriceId = plan.Id,

                    DurationInMonths = durationMonths,
                    Amount = amount,
                    Currency = currency,

                    ClientReturnUrl = string.IsNullOrWhiteSpace(req.ClientReturnUrl) ? null : req.ClientReturnUrl.Trim(),
                    CreatedAt = DateTime.UtcNow
                };

                _db.PaymentIntents.Add(intent);
                await _db.SaveChangesAsync(ct);

                var reference = $"LA-{intent.Id}-{Guid.NewGuid():N}"
                    .Substring(0, $"LA-{intent.Id}-".Length + 6);

                intent.ProviderReference = reference;
                intent.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                // API proxy return: /api/payments/paystack/return
                var apiBase = (_paystackOpts.ApiPublicBaseUrl ?? _paystackOpts.PublicBaseUrl ?? "").Trim();
                apiBase = apiBase.TrimEnd('/');

                var callbackUrl = !string.IsNullOrWhiteSpace(apiBase)
                    ? $"{apiBase}/api/payments/paystack/return"
                    : $"{Request.Scheme}://{Request.Host}/api/payments/paystack/return";

                try
                {
                    var init = await _paystack.InitializeTransactionAsync(
                        email: email.Trim(),
                        amountMajor: amount,
                        currency: currency,
                        reference: reference,
                        callbackUrl: callbackUrl,
                        ct: ct);

                    intent.ProviderReference = init.Reference;
                    intent.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);

                    return Ok(new CreateSubscriptionCheckoutResponseDto
                    {
                        PaymentIntentId = intent.Id,
                        Provider = intent.Provider,
                        Status = intent.Status.ToString(),
                        Amount = intent.Amount,
                        Currency = intent.Currency,
                        AuthorizationUrl = init.AuthorizationUrl,
                        Reference = init.Reference,
                        Message = "Redirect to Paystack to complete payment."
                    });
                }
                catch (Exception ex)
                {
                    intent.Status = PaymentStatus.Failed;
                    intent.ProviderResultDesc = "Paystack initialize failed";
                    intent.AdminNotes = SafeTrim(ex.Message, 500);
                    intent.UpdatedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync(ct);

                    return StatusCode(StatusCodes.Status409Conflict, new
                    {
                        message = "Paystack initialization failed. Please try again."
                    });
                }
            }

            return BadRequest(new { message = "Unsupported provider." });
        }

        // ======================================================
        // ADMIN: list subscriptions (monitoring)
        // GET /api/subscriptions/admin/list?status=Active&isTrial=true&page=1&pageSize=50&q=damaris
        // ======================================================
        [Authorize(Roles = "Admin")]
        [HttpGet("admin/list")]
        public async Task<IActionResult> AdminList(
            [FromQuery] SubscriptionStatus? status = null,
            [FromQuery] bool? isTrial = null,
            [FromQuery] bool includeExpiredWindows = false,
            [FromQuery] string? q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            EnsureGlobalAdmin(); // uses JWT claim (fast)

            var now = DateTime.UtcNow;

            page = page <= 0 ? 1 : page;
            pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 200);

            var query = _db.UserProductSubscriptions
                .AsNoTracking()
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(s => s.Status == status.Value);

            if (isTrial.HasValue)
                query = query.Where(s => s.IsTrial == isTrial.Value);

            if (!includeExpiredWindows)
            {
                query = query.Where(s =>
                    s.Status == SubscriptionStatus.Active &&
                    s.StartDate <= now &&
                    s.EndDate >= now);
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                q = q.Trim();
                query = query.Where(s =>
                    s.User.Username.Contains(q) ||
                    s.User.Email.Contains(q) ||
                    s.User.PhoneNumber.Contains(q) ||
                    s.ContentProduct.Name.Contains(q));
            }

            var total = await query.CountAsync(ct);

            var items = await query
                .OrderByDescending(s => s.EndDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new
                {
                    s.Id,
                    s.UserId,
                    user = new
                    {
                        s.User.FirstName,
                        s.User.LastName,
                        s.User.Username,
                        s.User.Email,
                        s.User.PhoneNumber,
                        s.User.UserType,
                        s.User.InstitutionId
                    },
                    s.ContentProductId,
                    productName = s.ContentProduct.Name,
                    s.Status,
                    s.IsTrial,
                    s.StartDate,
                    s.EndDate,
                    isActiveNow = (s.Status == SubscriptionStatus.Active && s.StartDate <= now && s.EndDate >= now),
                    grantedByUserId = s.GrantedByUserId
                })
                .ToListAsync(ct);

            return Ok(new
            {
                now,
                page,
                pageSize,
                total,
                items
            });
        }

        // ======================================================
        // ADMIN: suspend a user subscription (Global Admin)
        // POST /api/subscriptions/admin/{id}/suspend
        // Body: { "notes": "optional" }
        // ======================================================
        public class SubscriptionAdminActionRequest
        {
            public string? Notes { get; set; }
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("admin/{id:int}/suspend")]
        public async Task<IActionResult> Suspend(int id, [FromBody] SubscriptionAdminActionRequest? body, CancellationToken ct)
        {
            EnsureGlobalAdmin();

            var sub = await _db.UserProductSubscriptions
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (sub == null)
                return NotFound(new { message = "Subscription not found." });

            if (sub.Status == SubscriptionStatus.Suspended)
            {
                return Ok(new
                {
                    message = "Subscription is already suspended.",
                    id = sub.Id,
                    status = sub.Status
                });
            }

            sub.Status = SubscriptionStatus.Suspended;
            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                message = "Subscription suspended.",
                id = sub.Id,
                userId = sub.UserId,
                productName = sub.ContentProduct?.Name,
                status = sub.Status
            });
        }

        // ======================================================
        // ADMIN: reconcile expiry windows (Run now)
        // POST /api/subscriptions/admin/reconcile-expiry
        // ======================================================
        [Authorize(Roles = "Admin")]
        [HttpPost("admin/reconcile-expiry")]
        public async Task<IActionResult> AdminReconcileExpiry(
            [FromServices] ISubscriptionReconciler reconciler,
            CancellationToken ct)
        {
            EnsureGlobalAdmin();

            var res = await reconciler.ReconcileAsync(ct);

            return Ok(new
            {
                message = "Reconcile completed.",
                nowUtc = res.NowUtc,
                expiredCount = res.ExpiredCount
            });
        }

        // ======================================================
        // ADMIN: unsuspend a user subscription (Global Admin)
        // POST /api/subscriptions/admin/{id}/unsuspend
        // Body: { "notes": "optional" }
        // ======================================================
        [Authorize(Roles = "Admin")]
        [HttpPost("admin/{id:int}/unsuspend")]
        public async Task<IActionResult> Unsuspend(int id, [FromBody] SubscriptionAdminActionRequest? body, CancellationToken ct)
        {
            EnsureGlobalAdmin();

            var now = DateTime.UtcNow;

            var sub = await _db.UserProductSubscriptions
                .Include(s => s.User)
                .Include(s => s.ContentProduct)
                .FirstOrDefaultAsync(s => s.Id == id, ct);

            if (sub == null)
                return NotFound(new { message = "Subscription not found." });

            if (sub.Status != SubscriptionStatus.Suspended)
            {
                return Ok(new
                {
                    message = "Subscription is not suspended.",
                    id = sub.Id,
                    status = sub.Status
                });
            }

            var isWindowValid = sub.StartDate <= now && sub.EndDate >= now;
            sub.Status = isWindowValid ? SubscriptionStatus.Active : SubscriptionStatus.Expired;

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                message = isWindowValid ? "Subscription unsuspended (Active)." : "Subscription unsuspended (Expired window).",
                id = sub.Id,
                userId = sub.UserId,
                productName = sub.ContentProduct?.Name,
                status = sub.Status,
                windowValidNow = isWindowValid
            });
        }

        // =========================
        // Helpers
        // =========================
        private void EnsureGlobalAdmin()
        {
            var raw = User.FindFirstValue("isGlobalAdmin");
            if (!string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Global admin privileges required.");
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }
    }
}
