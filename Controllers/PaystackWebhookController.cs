using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Services;
using LawAfrica.API.Services.Documents; // ✅ NEW: for LegalDocumentPurchaseFulfillmentService
using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/payments/webhooks/paystack")]
    public class PaystackWebhookController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly PaystackOptions _opts;
        private readonly PaystackService _paystack;
        private readonly PaymentFinalizerService _finalizer;
        private readonly InvoiceNumberGenerator _invoiceNumberGenerator;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment; // ✅ NEW
        private readonly ILogger<PaystackWebhookController> _logger;

        public PaystackWebhookController(
            ApplicationDbContext db,
            IOptions<PaystackOptions> opts,
            PaystackService paystack,
            PaymentFinalizerService finalizer,
            InvoiceNumberGenerator invoiceNumberGenerator,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment, // ✅ NEW
            ILogger<PaystackWebhookController> logger)
        {
            _db = db;
            _opts = opts.Value;
            _paystack = paystack;
            _finalizer = finalizer;
            _invoiceNumberGenerator = invoiceNumberGenerator;
            _legalDocFulfillment = legalDocFulfillment; // ✅ NEW
            _logger = logger;
        }

        /// <summary>
        /// Paystack webhook endpoint. Must return 200 quickly and be idempotent.
        /// Stores webhook raw + provider transaction (verified) + invoice (autogen) + fulfills legal doc + finalizes.
        /// </summary>
        [AllowAnonymous]
        [HttpPost]
        public async Task<IActionResult> Receive(CancellationToken ct)
        {
            // 0) Read raw body (needed for signature validation + dedupe)
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogWarning("[PAYSTACK WEBHOOK] Empty body");
                return Ok();
            }

            var signature = Request.Headers["x-paystack-signature"].ToString();
            var signatureValid = !string.IsNullOrWhiteSpace(signature) && IsValidSignature(rawBody, signature, _opts.SecretKey);

            // 1) Parse event type + reference (best-effort; webhook audit stores raw regardless)
            string? eventType = null;
            string? reference = null;

            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;

                eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() : null;

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                {
                    reference = data.TryGetProperty("reference", out var r) ? r.GetString() : null;
                }
            }
            catch (Exception ex)
            {
                // We'll still store webhook row and mark failed parsing
                _logger.LogError(ex, "[PAYSTACK WEBHOOK] JSON parse failed");
            }

            eventType = (eventType ?? "").Trim();
            reference = reference?.Trim();

            // 2) Store webhook event (append-only + idempotent)
            var dedupeHash = ComputeSha256Hex($"paystack|{rawBody}");

            var hook = new PaymentProviderWebhookEvent
            {
                Provider = PaymentProvider.Paystack,
                EventType = string.IsNullOrWhiteSpace(eventType) ? "unknown" : eventType,
                Reference = reference,
                DedupeHash = dedupeHash,
                RawBody = rawBody,
                SignatureValid = signatureValid,
                ProcessingStatus = ProviderEventProcessingStatus.Received,
                ReceivedAt = DateTime.UtcNow
            };

            _db.PaymentProviderWebhookEvents.Add(hook);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException dbEx) when (IsUniqueViolation(dbEx))
            {
                // Duplicate webhook (retry) — safe to ignore
                _logger.LogInformation("[PAYSTACK WEBHOOK] Duplicate event dedupeHash={Hash} (ignored)", dedupeHash);
                return Ok();
            }

            // If signature invalid, mark event ignored/failed and stop
            if (!signatureValid)
            {
                hook.ProcessingStatus = ProviderEventProcessingStatus.Ignored;
                hook.ProcessingError = "Invalid or missing signature.";
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok();
            }

            // We require reference for matching
            if (string.IsNullOrWhiteSpace(reference))
            {
                hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                hook.ProcessingError = "Missing reference in payload.";
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok();
            }

            // Only process charge.success for value delivery
            if (!string.Equals(eventType, "charge.success", StringComparison.OrdinalIgnoreCase))
            {
                hook.ProcessingStatus = ProviderEventProcessingStatus.Ignored;
                hook.ProcessingError = $"Ignored event type: {eventType}";
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok();
            }

            // 3) Find PaymentIntent by ProviderReference
            var intent = await _db.PaymentIntents
                .FirstOrDefaultAsync(x => x.Provider == PaymentProvider.Paystack && x.ProviderReference == reference, ct);

            if (intent == null)
            {
                hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                hook.ProcessingError = $"No PaymentIntent found for reference={reference}";
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok();
            }

            // 4) Verify server-to-server (strong truth)
            try
            {
                var verify = await _paystack.VerifyTransactionAsync(reference, ct);

                // Always store verified payload (trim)
                intent.ProviderRawJson = SafeTrim(verify.RawJson, 4000);

                // Upsert provider transaction record (normalized)
                await UpsertProviderTransactionAsync(intent, verify, ct);

                if (!verify.IsSuccessful)
                {
                    intent.Status = PaymentStatus.Failed;
                    intent.ProviderResultDesc = $"Paystack verify status: {verify.Status}";
                    intent.UpdatedAt = DateTime.UtcNow;

                    hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                    hook.ProcessingError = $"Verify not successful: {verify.Status}";
                    hook.ProcessedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                // Validate currency/amount against intent
                if (!string.Equals(intent.Currency, verify.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    intent.Status = PaymentStatus.Failed;
                    intent.ProviderResultDesc = $"Currency mismatch. Intent={intent.Currency}, Paystack={verify.Currency}";
                    intent.UpdatedAt = DateTime.UtcNow;

                    hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                    hook.ProcessingError = "Currency mismatch.";
                    hook.ProcessedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                if (Math.Abs(intent.Amount - verify.AmountMajor) > 0.0001m)
                {
                    intent.Status = PaymentStatus.Failed;
                    intent.ProviderResultDesc = $"Amount mismatch. Intent={intent.Amount}, Paystack={verify.AmountMajor}";
                    intent.UpdatedAt = DateTime.UtcNow;

                    hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                    hook.ProcessingError = "Amount mismatch.";
                    hook.ProcessedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync(ct);
                    return Ok();
                }

                // Mark intent success + provider ids
                intent.Status = PaymentStatus.Success;
                intent.ProviderTransactionId = verify.ProviderTransactionId;
                intent.ProviderChannel = verify.Channel;
                intent.ProviderPaidAt = verify.PaidAt;
                intent.ProviderResultDesc = "Paystack payment verified";
                intent.UpdatedAt = DateTime.UtcNow;

                // 5) Ensure invoice exists (autogen serialized InvoiceNumber)
                await EnsureInvoiceForIntentAsync(intent, ct);

                await _db.SaveChangesAsync(ct);

                // ✅ NEW: Legal document fulfillment (Paystack parity with MPesa)
                if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                {
                    await _legalDocFulfillment.FulfillAsync(intent);
                    _logger.LogInformation("[PAYSTACK] Legal doc fulfillment done for PaymentIntentId={Id}", intent.Id);
                }

                // 6) Finalize (idempotent; don't break webhook on errors)
                try
                {
                    await _finalizer.FinalizeIfNeededAsync(intent.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[PAYSTACK WEBHOOK] Finalizer failed PaymentIntentId={Id}", intent.Id);
                    intent.AdminNotes = SafeTrim($"Finalizer error: {ex.Message}", 500);
                    intent.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                }

                hook.ProcessingStatus = ProviderEventProcessingStatus.Processed;
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("[PAYSTACK WEBHOOK] Processed PaymentIntentId={Id} ref={Ref}", intent.Id, reference);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PAYSTACK WEBHOOK] Processing error ref={Ref}", reference);

                hook.ProcessingStatus = ProviderEventProcessingStatus.Failed;
                hook.ProcessingError = SafeTrim(ex.Message, 500);
                hook.ProcessedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync(ct);
                return Ok();
            }
        }

        private async Task UpsertProviderTransactionAsync(PaymentIntent intent, PaystackService.VerifyResult verify, CancellationToken ct)
        {
            var providerTxId = !string.IsNullOrWhiteSpace(verify.ProviderTransactionId)
                ? verify.ProviderTransactionId!.Trim()
                : (intent.ProviderReference ?? "unknown");

            var status = verify.IsSuccessful ? ProviderTransactionStatus.Success : ProviderTransactionStatus.Failed;

            var tx = await _db.PaymentProviderTransactions
                .FirstOrDefaultAsync(x =>
                    x.Provider == PaymentProvider.Paystack &&
                    x.ProviderTransactionId == providerTxId,
                    ct);

            if (tx == null)
            {
                tx = new PaymentProviderTransaction
                {
                    Provider = PaymentProvider.Paystack,
                    ProviderTransactionId = providerTxId,
                    Reference = intent.ProviderReference ?? verify.Reference,
                    Status = status,
                    Amount = verify.AmountMajor,
                    Currency = verify.Currency,
                    Channel = verify.Channel,
                    PaidAt = verify.PaidAt,
                    RawJson = SafeTrim(verify.RawJson, 8000),
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                };

                _db.PaymentProviderTransactions.Add(tx);
            }
            else
            {
                tx.Reference = intent.ProviderReference ?? tx.Reference;
                tx.Status = status;
                tx.Amount = verify.AmountMajor;
                tx.Currency = verify.Currency;
                tx.Channel = verify.Channel;
                tx.PaidAt = verify.PaidAt;
                tx.RawJson = SafeTrim(verify.RawJson, 8000);
                tx.LastSeenAt = DateTime.UtcNow;
            }

            intent.ProviderTransactionId = providerTxId;
        }

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
                Status = InvoiceStatus.Paid, // verified payment
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

        private static bool IsValidSignature(string rawBody, string headerSignature, string secretKey)
        {
            if (string.IsNullOrWhiteSpace(secretKey)) return false;

            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var bodyBytes = Encoding.UTF8.GetBytes(rawBody);

            using var hmac = new HMACSHA512(keyBytes);
            var hash = hmac.ComputeHash(bodyBytes);
            var computed = Convert.ToHexString(hash).ToLowerInvariant();

            return string.Equals(computed, headerSignature.Trim().ToLowerInvariant(), StringComparison.Ordinal);
        }

        private static string ComputeSha256Hex(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static bool IsUniqueViolation(DbUpdateException ex)
        {
            if (ex.InnerException is PostgresException pg)
                return pg.SqlState == PostgresErrorCodes.UniqueViolation;

            return false;
        }

        private static string SafeTrim(string? value, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max);
        }

        //Resolve customer Name:

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
