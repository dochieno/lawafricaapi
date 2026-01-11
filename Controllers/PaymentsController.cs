using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Documents;
using LawAfrica.API.Models.DTOs.Payments;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents; // ✅ NEW (for DocumentEntitlementService)
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly MpesaService _mpesa;
        private readonly PaymentFinalizerService _finalizer;
        private readonly PaymentValidationService _paymentValidation;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment;
        private readonly ILogger<PaymentsController> _logger;

        private readonly DocumentEntitlementService _entitlement; // ✅ NEW
        private readonly InvoiceNumberGenerator _invoiceNumberGenerator; // ✅ NEW

        public PaymentsController(
            ApplicationDbContext db,
            MpesaService mpesa,
            PaymentFinalizerService finalizer,
            PaymentValidationService paymentValidation,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment,
            ILogger<PaymentsController> logger,
            DocumentEntitlementService entitlement, // ✅ NEW
            InvoiceNumberGenerator invoiceNumberGenerator // ✅ NEW
        )
        {
            _db = db;
            _mpesa = mpesa;
            _finalizer = finalizer;
            _paymentValidation = paymentValidation;
            _legalDocFulfillment = legalDocFulfillment;
            _logger = logger;

            _entitlement = entitlement; // ✅ NEW
            _invoiceNumberGenerator = invoiceNumberGenerator; // ✅ NEW
        }

        [AllowAnonymous]
        [HttpPost("mpesa/stk/initiate")]
        public async Task<IActionResult> InitiateStk([FromBody] InitiateMpesaCheckoutRequest request)
        {
            int? userId = null;

            if (request.Purpose != PaymentPurpose.PublicSignupFee)
            {
                if (!User.Identity?.IsAuthenticated ?? true)
                    return Unauthorized("Authentication required for this payment type.");

                userId = User.GetUserId();
            }

            try
            {
                await _paymentValidation.ValidateStkInitiateAsync(
                    request.Purpose,
                    request.Amount,
                    request.PhoneNumber,
                    request.RegistrationIntentId,
                    request.ContentProductId,
                    request.InstitutionId,
                    request.DurationInMonths,
                    request.LegalDocumentId
                );

                if (request.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                {
                    await ValidatePublicLegalDocumentPurchaseAsync(userId, request);
                }
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }

            string currency = "KES";
            if (request.Purpose == PaymentPurpose.PublicLegalDocumentPurchase && request.LegalDocumentId.HasValue)
            {
                var docCurrency = await _db.LegalDocuments
                    .AsNoTracking()
                    .Where(d => d.Id == request.LegalDocumentId.Value)
                    .Select(d => d.PublicCurrency)
                    .FirstOrDefaultAsync();

                if (!string.IsNullOrWhiteSpace(docCurrency))
                    currency = docCurrency!;
            }

            var intent = new PaymentIntent
            {
                Provider = PaymentProvider.Mpesa,
                Method = PaymentMethod.Mpesa,
                Purpose = request.Purpose,
                Status = PaymentStatus.Pending,
                Amount = request.Amount,
                Currency = currency,
                PhoneNumber = request.PhoneNumber,

                UserId = userId,

                InstitutionId = request.InstitutionId,
                RegistrationIntentId = request.RegistrationIntentId,
                ContentProductId = request.ContentProductId,
                DurationInMonths = request.DurationInMonths,

                LegalDocumentId = request.LegalDocumentId
            };

            _db.PaymentIntents.Add(intent);
            await _db.SaveChangesAsync();

            var token = await _mpesa.GetAccessTokenAsync();

            var (merchantRequestId, checkoutRequestId, raw) = await _mpesa.InitiateStkPushAsync(
                token,
                request.PhoneNumber,
                request.Amount,
                accountReference: $"LA-{intent.Id}",
                transactionDesc: $"{request.Purpose}"
            );

            intent.MerchantRequestId = merchantRequestId;
            intent.CheckoutRequestId = checkoutRequestId;
            intent.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                paymentIntentId = intent.Id,
                merchantRequestId,
                checkoutRequestId
            });
        }

        [AllowAnonymous]
        [HttpPost("mpesa/stk/callback")]
        public async Task<IActionResult> StkCallback(CancellationToken ct)
        {
            string raw = "";

            // ✅ NEW: webhook audit row created early (idempotent via dedupe hash)
            PaymentProviderWebhookEvent? hook = null;

            try
            {
                using var reader = new StreamReader(Request.Body);
                raw = await reader.ReadToEndAsync();

                _logger.LogWarning("[MPESA CALLBACK HIT] Raw length={Len}", raw?.Length ?? 0);

                if (string.IsNullOrWhiteSpace(raw))
                {
                    _logger.LogWarning("[MPESA CALLBACK] Empty body.");
                    return Ok();
                }

                // ✅ NEW: store raw webhook event (append-only + idempotent)
                var dedupeHash = ComputeSha256Hex($"mpesa|{raw}");
                hook = new PaymentProviderWebhookEvent
                {
                    Provider = PaymentProvider.Mpesa,
                    EventType = "stkCallback",
                    DedupeHash = dedupeHash,
                    RawBody = raw,
                    SignatureValid = null, // MPesa STK callback typically doesn't include HMAC signature header in this flow
                    ProcessingStatus = ProviderEventProcessingStatus.Received,
                    ReceivedAt = DateTime.UtcNow
                };

                _db.PaymentProviderWebhookEvents.Add(hook);

                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    _logger.LogInformation("[MPESA CALLBACK] Duplicate webhook received (dedupe) - ignoring.");
                    return Ok();
                }

                using var doc = JsonDocument.Parse(raw);

                if (!doc.RootElement.TryGetProperty("Body", out var body))
                {
                    _logger.LogWarning("[MPESA CALLBACK] Missing Body. Raw={Raw}", raw);
                    MarkHook(hook, ProviderEventProcessingStatus.Failed, "Missing Body", reference: null);
                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                if (!(body.TryGetProperty("StkCallback", out var stk) ||
                      body.TryGetProperty("stkCallback", out stk)))
                {
                    _logger.LogWarning("[MPESA CALLBACK] Missing StkCallback. Raw={Raw}", raw);
                    MarkHook(hook, ProviderEventProcessingStatus.Failed, "Missing StkCallback", reference: null);
                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                string? checkoutRequestId = null;

                if (stk.TryGetProperty("CheckoutRequestID", out var cr1))
                    checkoutRequestId = cr1.GetString();

                if (string.IsNullOrWhiteSpace(checkoutRequestId) &&
                    stk.TryGetProperty("CheckoutRequestId", out var cr2))
                    checkoutRequestId = cr2.GetString();

                checkoutRequestId = checkoutRequestId?.Trim();

                if (string.IsNullOrWhiteSpace(checkoutRequestId))
                {
                    _logger.LogWarning("[MPESA CALLBACK] Missing CheckoutRequestID. Raw={Raw}", raw);
                    MarkHook(hook, ProviderEventProcessingStatus.Failed, "Missing CheckoutRequestID", reference: null);
                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                // ✅ NEW: store a reference on the hook for easier matching/search
                hook.Reference = checkoutRequestId;

                var resultCode = stk.TryGetProperty("ResultCode", out var rc) ? rc.GetInt32() : -999;
                var resultDesc = stk.TryGetProperty("ResultDesc", out var rd) ? rd.GetString() : null;

                _logger.LogWarning("[MPESA CALLBACK] checkoutRequestId={Checkout} resultCode={Code} desc={Desc}",
                    checkoutRequestId, resultCode, resultDesc);

                var intent = await _db.PaymentIntents
                    .FirstOrDefaultAsync(p => p.CheckoutRequestId == checkoutRequestId, ct);

                if (intent == null)
                {
                    _logger.LogError("[MPESA CALLBACK] No PaymentIntent matches checkoutRequestId={Checkout}. Raw={Raw}",
                        checkoutRequestId, raw);

                    MarkHook(hook, ProviderEventProcessingStatus.Failed, $"No PaymentIntent for CheckoutRequestID={checkoutRequestId}", checkoutRequestId);
                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                if (intent.IsFinalized)
                {
                    _logger.LogWarning("[MPESA CALLBACK] Already finalized PaymentIntentId={Id}", intent.Id);

                    // ✅ NEW: mark webhook as processed (idempotent)
                    MarkHook(hook, ProviderEventProcessingStatus.Processed, "Already finalized", checkoutRequestId);
                    await _db.SaveChangesAsync(ct);

                    return Ok();
                }

                intent.ProviderResultCode = resultCode.ToString();
                intent.ProviderResultDesc = resultDesc;
                intent.UpdatedAt = DateTime.UtcNow;

                if (resultCode == 0)
                {
                    intent.Status = PaymentStatus.Success;

                    if (stk.TryGetProperty("CallbackMetadata", out var meta) &&
                        meta.TryGetProperty("Item", out var items) &&
                        items.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in items.EnumerateArray())
                        {
                            var name = item.TryGetProperty("Name", out var n) ? n.GetString() : null;
                            if (name == "MpesaReceiptNumber")
                            {
                                intent.MpesaReceiptNumber = item.TryGetProperty("Value", out var v)
                                    ? v.ToString()
                                    : null;
                                break;
                            }
                        }
                    }

                    // ✅ NEW: set normalized provider transaction fields
                    intent.ProviderTransactionId = !string.IsNullOrWhiteSpace(intent.MpesaReceiptNumber)
                        ? intent.MpesaReceiptNumber
                        : intent.ProviderTransactionId;

                    intent.ProviderChannel = "Mpesa";
                    intent.ProviderPaidAt = DateTime.UtcNow;
                }
                else
                {
                    intent.Status = PaymentStatus.Failed;
                }

                await _db.SaveChangesAsync(ct);

                _logger.LogWarning("[MPESA CALLBACK] Updated PaymentIntentId={Id} Status={Status} Receipt={Receipt}",
                    intent.Id, intent.Status, intent.MpesaReceiptNumber);

                // ✅ NEW: Upsert provider transaction (on success)
                if (intent.Status == PaymentStatus.Success)
                {
                    await UpsertMpesaProviderTransactionAsync(intent, checkoutRequestId, raw, ct);

                    // ✅ NEW: Ensure invoice exists (autogen invoice number)
                    await EnsureInvoiceForIntentAsync(intent, ct);

                    await _db.SaveChangesAsync(ct);
                }

                if (intent.Status == PaymentStatus.Success &&
                    intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                {
                    await _legalDocFulfillment.FulfillAsync(intent);
                    _logger.LogWarning("[MPESA CALLBACK] Legal doc fulfillment done for PaymentIntentId={Id}", intent.Id);
                }

                await _finalizer.FinalizeIfNeededAsync(intent.Id);

                _logger.LogWarning("[MPESA CALLBACK] Finalizer completed for PaymentIntentId={Id}", intent.Id);

                // ✅ NEW: mark webhook event processed
                MarkHook(hook, ProviderEventProcessingStatus.Processed, null, checkoutRequestId);
                await _db.SaveChangesAsync(ct);

                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MPESA CALLBACK ERROR] Raw={Raw}", raw);

                // ✅ NEW: record failure on webhook audit row (best-effort)
                try
                {
                    if (hook != null)
                    {
                        MarkHook(hook, ProviderEventProcessingStatus.Failed, SafeTrim(ex.Message, 500), hook.Reference);
                        await _db.SaveChangesAsync(ct);
                    }
                }
                catch { /* swallow */ }

                return Ok();
            }
        }

        [AllowAnonymous]
        [HttpGet("intent/{paymentIntentId}")]
        public async Task<IActionResult> GetPaymentIntent(int paymentIntentId)
        {
            var intent = await _db.PaymentIntents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == paymentIntentId);

            if (intent == null) return NotFound("Payment intent not found.");

            return Ok(new
            {
                intent.Id,
                intent.Status,
                intent.IsFinalized,
                intent.RegistrationIntentId,
                intent.ContentProductId,
                intent.InstitutionId,

                intent.LegalDocumentId,

                intent.CheckoutRequestId,
                intent.MerchantRequestId,
                intent.ProviderResultCode,
                intent.ProviderResultDesc,
                intent.MpesaReceiptNumber,
                intent.UpdatedAt,

                // ✅ NEW (helpful for debugging)
                intent.InvoiceId,
                intent.ProviderTransactionId,
                intent.ProviderPaidAt
            });
        }

        // --------------------------------------------------------------------
        // ✅ Updated validation for PublicLegalDocumentPurchase:
        // - Public individual: allowed
        // - Admin: allowed (optional)
        // - Institution user: allowed IF decision.CanPurchaseIndividually == true
        //   (covers BOTH InstitutionSubscriptionInactive AND InstitutionSeatLimitExceeded, etc.)
        // --------------------------------------------------------------------
        private async Task ValidatePublicLegalDocumentPurchaseAsync(int? userId, InitiateMpesaCheckoutRequest request)
        {
            if (!userId.HasValue || userId.Value <= 0)
                throw new InvalidOperationException("Authentication required for legal document purchase.");

            if (!request.LegalDocumentId.HasValue)
                throw new InvalidOperationException("LegalDocumentId is required for legal document purchase.");

            var user = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId.Value)
                .Select(u => new { u.Id, u.UserType, u.InstitutionId })
                .FirstOrDefaultAsync();

            if (user == null)
                throw new InvalidOperationException("User not found.");

            var doc = await _db.LegalDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == request.LegalDocumentId.Value && d.Status == LegalDocumentStatus.Published);

            if (doc == null)
                throw new InvalidOperationException("Document not found or unpublished.");

            if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
                throw new InvalidOperationException("This document is not available for purchase.");

            if (request.Amount != doc.PublicPrice.Value)
                throw new InvalidOperationException("Amount does not match the document price.");

            var already = await _db.UserLegalDocumentPurchases
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId.Value && p.LegalDocumentId == request.LegalDocumentId.Value);

            if (already)
                throw new InvalidOperationException("Already purchased.");

            // ✅ Public individual allowed
            var isPublicIndividual = user.UserType == UserType.Public && user.InstitutionId == null;
            if (isPublicIndividual)
                return;

            // ✅ Admin can test (optional)
            if (user.UserType == UserType.Admin)
                return;

            // ✅ Institution-user path (policy-driven)
            if (user.InstitutionId != null)
            {
                var decision = await _entitlement.GetEntitlementDecisionAsync(userId.Value, doc);

                // If subscription is active and user already has full access -> block purchase
                if (decision.AccessLevel == DocumentAccessLevel.FullAccess)
                    throw new InvalidOperationException("This document is included in your institution subscription.");

                // ✅ Allow purchase if policy allows (covers seat-exceeded + inactive + other restricted states)
                if (!decision.CanPurchaseIndividually)
                    throw new InvalidOperationException(
                        decision.PurchaseDisabledReason
                        ?? "Purchases are disabled for institution accounts. Please contact your administrator."
                    );

                return;
            }

            // Other non-public non-institution accounts => deny
            throw new InvalidOperationException("Only public individual accounts can purchase documents.");
        }

        // =======================
        // ✅ NEW HELPERS (MPESA -> reconciliation tables + invoices)
        // =======================

        private async Task UpsertMpesaProviderTransactionAsync(PaymentIntent intent, string checkoutRequestId, string rawJson, CancellationToken ct)
        {
            // We prefer receipt number as ProviderTransactionId, but fall back to checkoutRequestId if missing.
            var providerTxId = !string.IsNullOrWhiteSpace(intent.MpesaReceiptNumber)
                ? intent.MpesaReceiptNumber!.Trim()
                : checkoutRequestId;

            var existing = await _db.PaymentProviderTransactions
                .FirstOrDefaultAsync(x => x.Provider == PaymentProvider.Mpesa && x.ProviderTransactionId == providerTxId, ct);

            if (existing == null)
            {
                existing = new PaymentProviderTransaction
                {
                    Provider = PaymentProvider.Mpesa,
                    ProviderTransactionId = providerTxId,
                    Reference = checkoutRequestId,
                    Status = ProviderTransactionStatus.Success,
                    Amount = intent.Amount,
                    Currency = intent.Currency,
                    Channel = "Mpesa",
                    PaidAt = intent.ProviderPaidAt ?? DateTime.UtcNow,
                    RawJson = SafeTrim(rawJson, 8000),
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                };
                _db.PaymentProviderTransactions.Add(existing);
            }
            else
            {
                existing.Reference = checkoutRequestId;
                existing.Status = ProviderTransactionStatus.Success;
                existing.Amount = intent.Amount;
                existing.Currency = intent.Currency;
                existing.Channel = "Mpesa";
                existing.PaidAt = intent.ProviderPaidAt ?? existing.PaidAt;
                existing.RawJson = SafeTrim(rawJson, 8000);
                existing.LastSeenAt = DateTime.UtcNow;
            }
        }

        //Invoice Creation
        private async Task EnsureInvoiceForIntentAsync(PaymentIntent intent, CancellationToken ct)
        {
            // Only invoice successful payments
            if (intent.Status != PaymentStatus.Success)
                return;

            // ✅ If invoice already linked, backfill CustomerName if missing (idempotent)
            if (intent.InvoiceId.HasValue && intent.InvoiceId.Value > 0)
            {
                var existingInvoice = await _db.Invoices
                    .FirstOrDefaultAsync(x => x.Id == intent.InvoiceId.Value, ct);

                if (existingInvoice != null && string.IsNullOrWhiteSpace(existingInvoice.CustomerName))
                {
                    existingInvoice.CustomerName = await ResolveInvoiceCustomerNameAsync(intent, ct);
                    existingInvoice.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }

                return;
            }

            var invoiceNo = await _invoiceNumberGenerator.GenerateAsync(ct);

            var invoice = new Invoice
            {
                InvoiceNumber = invoiceNo,

                Status = InvoiceStatus.Paid,
                Purpose = intent.Purpose,

                Currency = intent.Currency,
                Subtotal = intent.Amount,
                TaxTotal = 0m,
                DiscountTotal = 0m,
                Total = intent.Amount,

                AmountPaid = intent.Amount,
                PaidAt = intent.ProviderPaidAt ?? DateTime.UtcNow,

                InstitutionId = intent.InstitutionId,
                UserId = intent.UserId,

                // ✅ NEW: set customer name (reconciliation)
                CustomerName = await ResolveInvoiceCustomerNameAsync(intent, ct),

                IssuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            invoice.Lines.Add(new InvoiceLine
            {
                Description = BuildLineDescription(intent),
                ItemCode = BuildItemCode(intent),
                Quantity = 1m,
                UnitPrice = intent.Amount,
                LineSubtotal = intent.Amount,
                TaxAmount = 0m,
                DiscountAmount = 0m,
                LineTotal = intent.Amount,
                ContentProductId = intent.ContentProductId,
                LegalDocumentId = intent.LegalDocumentId
            });

            _db.Invoices.Add(invoice);
            await _db.SaveChangesAsync(ct);

            intent.InvoiceId = invoice.Id;
            intent.UpdatedAt = DateTime.UtcNow;
        }

        private static string BuildLineDescription(PaymentIntent intent)
        {
            var parts = new List<string> { intent.Purpose.ToString() };

            if (intent.ContentProductId.HasValue) parts.Add($"ContentProductId={intent.ContentProductId.Value}");
            if (intent.LegalDocumentId.HasValue) parts.Add($"LegalDocumentId={intent.LegalDocumentId.Value}");
            if (intent.DurationInMonths.HasValue) parts.Add($"DurationMonths={intent.DurationInMonths.Value}");
            if (intent.InstitutionId.HasValue) parts.Add($"InstitutionId={intent.InstitutionId.Value}");

            return string.Join(" | ", parts);
        }

        private static string BuildItemCode(PaymentIntent intent)
        {
            if (intent.Purpose == PaymentPurpose.InstitutionProductSubscription) return "SUBSCRIPTION";
            if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase) return "LEGALDOC";
            if (intent.Purpose == PaymentPurpose.PublicSignupFee) return "SIGNUP";
            return "PAYMENT";
        }

        private static void MarkHook(PaymentProviderWebhookEvent hook, ProviderEventProcessingStatus status, string? error, string? reference)
        {
            hook.ProcessingStatus = status;
            hook.ProcessingError = SafeTrim(error, 500);
            hook.Reference = reference ?? hook.Reference;
            hook.ProcessedAt = DateTime.UtcNow;
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            return ex.InnerException is PostgresException pg &&
                   pg.SqlState == PostgresErrorCodes.UniqueViolation;
        }

        private static string ComputeSha256Hex(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private async Task<string?> ResolveInvoiceCustomerNameAsync(PaymentIntent intent, CancellationToken ct)
        {
            // Rule 1: Institution subscription invoices -> institution name
            if (intent.Purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                if (intent.InstitutionId.HasValue && intent.InstitutionId.Value > 0)
                {
                    var instName = await _db.Institutions
                        .AsNoTracking()
                        .Where(x => x.Id == intent.InstitutionId.Value)
                        .Select(x => x.Name)
                        .FirstOrDefaultAsync(ct);

                    if (!string.IsNullOrWhiteSpace(instName))
                        return instName.Trim();
                }

                return "Institution";
            }

            // Rule 2: Public signup fee -> registration intent FirstName + LastName
            if (intent.Purpose == PaymentPurpose.PublicSignupFee)
            {
                if (intent.RegistrationIntentId.HasValue && intent.RegistrationIntentId.Value > 0)
                {
                    var reg = await _db.RegistrationIntents
                        .AsNoTracking()
                        .Where(x => x.Id == intent.RegistrationIntentId.Value)
                        .Select(x => new { x.FirstName, x.LastName, x.Username, x.Email })
                        .FirstOrDefaultAsync(ct);

                    if (reg != null)
                    {
                        var full = JoinName(reg.FirstName, reg.LastName);
                        if (!string.IsNullOrWhiteSpace(full)) return full;

                        if (!string.IsNullOrWhiteSpace(reg.Username)) return reg.Username.Trim();
                        if (!string.IsNullOrWhiteSpace(reg.Email)) return reg.Email.Trim();
                    }
                }

                // fallback (if reg intent missing)
                return "Public User";
            }

            // Rule 3: All other public purchases -> authenticated user name
            if (intent.UserId.HasValue && intent.UserId.Value > 0)
            {
                var u = await _db.Users
                    .AsNoTracking()
                    .Where(x => x.Id == intent.UserId.Value)
                    .Select(x => new { x.FirstName, x.LastName, x.Username, x.Email })
                    .FirstOrDefaultAsync(ct);

                if (u != null)
                {
                    var full = JoinName(u.FirstName, u.LastName);
                    if (!string.IsNullOrWhiteSpace(full)) return full;

                    if (!string.IsNullOrWhiteSpace(u.Username)) return u.Username.Trim();
                    if (!string.IsNullOrWhiteSpace(u.Email)) return u.Email.Trim();
                }
            }

            // fallback
            return null;
        }

        private static string? JoinName(string? first, string? last)
        {
            var f = (first ?? "").Trim();
            var l = (last ?? "").Trim();
            var full = $"{f} {l}".Trim();
            return string.IsNullOrWhiteSpace(full) ? null : full;
        }

    }
}
