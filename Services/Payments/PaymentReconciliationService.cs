using LawAfrica.API.Data;
using LawAfrica.API.Models.Payments;
using LawAfrica.API.Models.DTOs.Payments.Reconciliation;
using LawAfrica.API.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    public class PaymentReconciliationService
    {
        private readonly ApplicationDbContext _db;
        private readonly InvoiceNumberGenerator _invoiceNumberGenerator;
        private readonly PaymentFinalizerService _finalizer;
        private readonly LegalDocumentPurchaseFulfillmentService _legalDocFulfillment;

        public PaymentReconciliationService(
            ApplicationDbContext db,
            InvoiceNumberGenerator invoiceNumberGenerator,
            PaymentFinalizerService finalizer,
            LegalDocumentPurchaseFulfillmentService legalDocFulfillment)
        {
            _db = db;
            _invoiceNumberGenerator = invoiceNumberGenerator;
            _finalizer = finalizer;
            _legalDocFulfillment = legalDocFulfillment;
        }

        public async Task<ReconciliationRunResponse> RunAsync(
            RunReconciliationRequest request,
            int performedByUserId,
            CancellationToken ct)
        {
            if (request.ToUtc <= request.FromUtc)
                throw new InvalidOperationException("ToUtc must be greater than FromUtc.");

            var run = new PaymentReconciliationRun
            {
                Provider = request.Provider,
                FromUtc = request.FromUtc,
                ToUtc = request.ToUtc,
                PerformedByUserId = performedByUserId,
                Mode = "Auto",
                CreatedAt = DateTime.UtcNow
            };

            _db.PaymentReconciliationRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            var providers = request.Provider.HasValue
                ? new[] { request.Provider.Value }
                : Enum.GetValues<PaymentProvider>();

            // Pull intents in window (use ProviderPaidAt when present, otherwise UpdatedAt/CreatedAt)
            var intentsQuery = _db.PaymentIntents.AsNoTracking().Where(i =>
                (i.ProviderPaidAt ?? i.UpdatedAt ?? i.CreatedAt) >= request.FromUtc &&
                (i.ProviderPaidAt ?? i.UpdatedAt ?? i.CreatedAt) <= request.ToUtc);

            // If provider filter specified, apply
            if (request.Provider.HasValue)
                intentsQuery = intentsQuery.Where(i => i.Provider == request.Provider.Value);

            var intents = await intentsQuery.ToListAsync(ct);

            // Pull provider transactions in window (PaidAt if present else LastSeenAt)
            var txQuery = _db.PaymentProviderTransactions.AsNoTracking().Where(t =>
                (t.PaidAt ?? t.LastSeenAt) >= request.FromUtc &&
                (t.PaidAt ?? t.LastSeenAt) <= request.ToUtc);

            if (request.Provider.HasValue)
                txQuery = txQuery.Where(t => t.Provider == request.Provider.Value);

            var txs = await txQuery.ToListAsync(ct);

            // Build lookup maps
            // ProviderTransactionId -> tx (duplicates detected)
            var txById = txs
                .Where(t => !string.IsNullOrWhiteSpace(t.ProviderTransactionId))
                .GroupBy(t => $"{t.Provider}|{t.ProviderTransactionId}".ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            // Provider+Reference -> tx
            var txByRef = txs
                .Where(t => !string.IsNullOrWhiteSpace(t.Reference))
                .GroupBy(t => $"{t.Provider}|{t.Reference}".ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.ToList());

            // Intent matching keys (transactionId or reference)
            string IntentTxKey(PaymentIntent i) =>
                $"{i.Provider}|{(i.ProviderTransactionId ?? "").Trim()}".ToLowerInvariant();

            string IntentRefKey(PaymentIntent i)
            {
                // Paystack uses ProviderReference; Mpesa uses CheckoutRequestId (as reference)
                var r = i.Provider == PaymentProvider.Mpesa
                    ? i.CheckoutRequestId
                    : i.ProviderReference;

                return $"{i.Provider}|{(r ?? "").Trim()}".ToLowerInvariant();
            }

            // Duplicate intents by same provider reference (helps detect multiple intents per same provider reference)
            var intentsByRef = intents
                .Where(i => !string.IsNullOrWhiteSpace(i.ProviderReference) || !string.IsNullOrWhiteSpace(i.CheckoutRequestId))
                .GroupBy(i => IntentRefKey(i))
                .ToDictionary(g => g.Key, g => g.ToList());

            var items = new List<PaymentReconciliationItem>();

            // A) For each provider transaction, ensure internal intent exists (provider-first mismatch detection)
            foreach (var t in txs)
            {
                var candidates = new List<PaymentIntent>();

                // Match by tx id
                var txIdKey = $"{t.Provider}|{t.ProviderTransactionId}".ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(t.ProviderTransactionId))
                    candidates.AddRange(intents.Where(i => IntentTxKey(i) == txIdKey));

                // Match by reference (fallback)
                var refKey = $"{t.Provider}|{t.Reference}".ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(t.Reference))
                    candidates.AddRange(intents.Where(i => IntentRefKey(i) == refKey));

                candidates = candidates.DistinctBy(x => x.Id).ToList();

                if (candidates.Count == 0)
                {
                    items.Add(new PaymentReconciliationItem
                    {
                        RunId = run.Id,
                        Provider = t.Provider,
                        Reference = t.Reference,
                        ProviderTransactionIdRef = t.Id,
                        Status = ReconciliationStatus.MissingInternalIntent,
                        Reason = ReconciliationReason.NoPaymentIntentForReference,
                        Details = $"Provider transaction exists but no internal PaymentIntent matched. TxId={t.ProviderTransactionId}",
                        CreatedAt = DateTime.UtcNow
                    });
                    continue;
                }

                if (candidates.Count > 1)
                {
                    items.Add(new PaymentReconciliationItem
                    {
                        RunId = run.Id,
                        Provider = t.Provider,
                        Reference = t.Reference,
                        ProviderTransactionIdRef = t.Id,
                        Status = ReconciliationStatus.Duplicate,
                        Reason = ReconciliationReason.DuplicateReference,
                        Details = $"Multiple PaymentIntents match provider transaction. IntentIds={string.Join(",", candidates.Select(x => x.Id))}",
                        CreatedAt = DateTime.UtcNow
                    });
                    continue;
                }

                // Evaluate match quality
                var intent = candidates[0];
                var status = ReconciliationStatus.Matched;
                var reason = ReconciliationReason.None;
                var details = "Matched provider transaction to payment intent.";

                if (!string.Equals(intent.Currency, t.Currency, StringComparison.OrdinalIgnoreCase))
                {
                    status = ReconciliationStatus.Mismatch;
                    reason = ReconciliationReason.CurrencyMismatch;
                    details = $"Currency mismatch. Intent={intent.Currency}, Provider={t.Currency}";
                }
                else if (Math.Abs(intent.Amount - t.Amount) > 0.0001m)
                {
                    status = ReconciliationStatus.Mismatch;
                    reason = ReconciliationReason.AmountMismatch;
                    details = $"Amount mismatch. Intent={intent.Amount}, Provider={t.Amount}";
                }
                else if (intent.Status != PaymentStatus.Success && t.Status == ProviderTransactionStatus.Success)
                {
                    status = ReconciliationStatus.NeedsReview;
                    reason = ReconciliationReason.StatusMismatch;
                    details = $"Provider says Success but intent status is {intent.Status}.";
                }
                else if (intent.Status == PaymentStatus.Success && !intent.IsFinalized)
                {
                    status = ReconciliationStatus.FinalizerFailed;
                    reason = ReconciliationReason.FinalizationError;
                    details = $"Intent is Success but not finalized. Check AdminNotes/Finalizer logs.";
                }

                items.Add(new PaymentReconciliationItem
                {
                    RunId = run.Id,
                    Provider = t.Provider,
                    Reference = t.Reference,
                    PaymentIntentId = intent.Id,
                    ProviderTransactionIdRef = t.Id,
                    InvoiceId = intent.InvoiceId,
                    Status = status,
                    Reason = reason,
                    Details = details,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // B) For each successful intent, ensure provider transaction exists (internal-first mismatch detection)
            foreach (var intent in intents.Where(i => i.Status == PaymentStatus.Success))
            {
                var found = false;

                // Find by provider tx id
                if (!string.IsNullOrWhiteSpace(intent.ProviderTransactionId))
                {
                    var key = $"{intent.Provider}|{intent.ProviderTransactionId}".ToLowerInvariant();
                    if (txById.TryGetValue(key, out var list) && list.Count > 0)
                        found = true;
                }

                // Find by reference fallback
                if (!found)
                {
                    var refKey = IntentRefKey(intent);
                    if (txByRef.TryGetValue(refKey, out var list) && list.Count > 0)
                        found = true;
                }

                if (!found)
                {
                    items.Add(new PaymentReconciliationItem
                    {
                        RunId = run.Id,
                        Provider = intent.Provider,
                        Reference = intent.Provider == PaymentProvider.Mpesa ? intent.CheckoutRequestId : intent.ProviderReference,
                        PaymentIntentId = intent.Id,
                        InvoiceId = intent.InvoiceId,
                        Status = ReconciliationStatus.MissingProviderTransaction,
                        Reason = ReconciliationReason.NoProviderTransactionForIntent,
                        Details = "Internal intent is Success but no provider transaction row exists (verify pipeline may have failed).",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Duplicates by reference
                var intentRefKey = IntentRefKey(intent);
                if (intentsByRef.TryGetValue(intentRefKey, out var dupList) && dupList.Count > 1)
                {
                    items.Add(new PaymentReconciliationItem
                    {
                        RunId = run.Id,
                        Provider = intent.Provider,
                        Reference = intent.Provider == PaymentProvider.Mpesa ? intent.CheckoutRequestId : intent.ProviderReference,
                        PaymentIntentId = intent.Id,
                        InvoiceId = intent.InvoiceId,
                        Status = ReconciliationStatus.Duplicate,
                        Reason = ReconciliationReason.DuplicateReference,
                        Details = $"Multiple intents share same provider reference. IntentIds={string.Join(",", dupList.Select(x => x.Id))}",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _db.PaymentReconciliationItems.AddRange(items);
            await _db.SaveChangesAsync(ct);

            return BuildRunResponse(run, items);
        }

        public async Task<long> ManualReconcileAsync(ManualReconcileRequest request, int performedByUserId, CancellationToken ct)
        {
            if (request.PaymentIntentId <= 0)
                throw new InvalidOperationException("PaymentIntentId is required.");

            if (request.Amount <= 0)
                throw new InvalidOperationException("Amount must be greater than zero.");

            if (string.IsNullOrWhiteSpace(request.Currency))
                throw new InvalidOperationException("Currency is required.");

            if (string.IsNullOrWhiteSpace(request.Reference) && string.IsNullOrWhiteSpace(request.ProviderTransactionId))
                throw new InvalidOperationException("Provide Reference or ProviderTransactionId.");

            var intent = await _db.PaymentIntents.FirstOrDefaultAsync(x => x.Id == request.PaymentIntentId, ct);
            if (intent == null)
                throw new InvalidOperationException("Payment intent not found.");

            // Create a manual run (append-only audit)
            var run = new PaymentReconciliationRun
            {
                Provider = request.Provider,
                FromUtc = request.PaidAtUtc.AddMinutes(-5),
                ToUtc = request.PaidAtUtc.AddMinutes(5),
                PerformedByUserId = performedByUserId,
                Mode = "Manual",
                CreatedAt = DateTime.UtcNow
            };

            _db.PaymentReconciliationRuns.Add(run);
            await _db.SaveChangesAsync(ct);

            // Upsert provider transaction row (manual)
            var providerTxId = (request.ProviderTransactionId ?? request.Reference ?? "").Trim();
            var reference = (request.Reference ?? intent.ProviderReference ?? intent.CheckoutRequestId ?? "").Trim();

            var providerTx = await _db.PaymentProviderTransactions
                .FirstOrDefaultAsync(x =>
                    x.Provider == request.Provider &&
                    x.ProviderTransactionId == providerTxId, ct);

            if (providerTx == null)
            {
                providerTx = new PaymentProviderTransaction
                {
                    Provider = request.Provider,
                    ProviderTransactionId = providerTxId,
                    Reference = reference,
                    Status = ProviderTransactionStatus.Success,
                    Amount = request.Amount,
                    Currency = request.Currency,
                    Channel = request.Channel,
                    PaidAt = request.PaidAtUtc,
                    RawJson = "",
                    FirstSeenAt = DateTime.UtcNow,
                    LastSeenAt = DateTime.UtcNow
                };
                _db.PaymentProviderTransactions.Add(providerTx);
            }
            else
            {
                providerTx.Reference = reference;
                providerTx.Status = ProviderTransactionStatus.Success;
                providerTx.Amount = request.Amount;
                providerTx.Currency = request.Currency;
                providerTx.Channel = request.Channel;
                providerTx.PaidAt = request.PaidAtUtc;
                providerTx.LastSeenAt = DateTime.UtcNow;
            }

            // Update intent to Success if needed (don’t overwrite critical ids; only fill blanks / set by admin)
            intent.Provider = request.Provider;
            intent.Status = PaymentStatus.Success;
            intent.Currency = request.Currency;
            intent.Amount = request.Amount;
            intent.ProviderPaidAt = request.PaidAtUtc;
            intent.ProviderChannel = request.Channel ?? intent.ProviderChannel;
            intent.ProviderTransactionId = request.ProviderTransactionId ?? intent.ProviderTransactionId;

            if (request.Provider == PaymentProvider.Paystack && !string.IsNullOrWhiteSpace(request.Reference))
                intent.ProviderReference = request.Reference;

            if (request.Provider == PaymentProvider.Mpesa && !string.IsNullOrWhiteSpace(request.Reference))
                intent.CheckoutRequestId = request.Reference;

            intent.AdminNotes = string.IsNullOrWhiteSpace(request.Notes)
                ? (intent.AdminNotes ?? "Manual reconcile applied.")
                : $"{(intent.AdminNotes ?? "").Trim()} | Manual reconcile: {request.Notes}".Trim();

            intent.UpdatedAt = DateTime.UtcNow;

            // Ensure invoice
            await EnsureInvoiceForIntentAsync(intent, ct);

            await _db.SaveChangesAsync(ct);

            // Fulfillment + finalizer (idempotent)
            if (intent.Purpose == PaymentPurpose.PublicLegalDocumentPurchase)
                await _legalDocFulfillment.FulfillAsync(intent);

            await _finalizer.FinalizeIfNeededAsync(intent.Id);

            // Record manual reconciliation item
            _db.PaymentReconciliationItems.Add(new PaymentReconciliationItem
            {
                RunId = run.Id,
                Provider = request.Provider,
                Reference = reference,
                PaymentIntentId = intent.Id,
                ProviderTransactionIdRef = providerTx.Id,
                InvoiceId = intent.InvoiceId,
                Status = ReconciliationStatus.ManuallyResolved,
                Reason = ReconciliationReason.ManualOverride,
                Details = string.IsNullOrWhiteSpace(request.Notes) ? "Manual reconcile applied." : request.Notes,
                CreatedAt = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
            return run.Id;
        }

        public async Task<ReconciliationReportResponse> GetReportAsync(
            PaymentProvider? provider,
            DateTime fromUtc,
            DateTime toUtc,
            ReconciliationStatus? status,
            ReconciliationReason? reason,
            int skip,
            int take,
            CancellationToken ct)
        {
            if (toUtc <= fromUtc)
                throw new InvalidOperationException("toUtc must be greater than fromUtc.");

            if (take <= 0) take = 50;
            if (take > 200) take = 200;
            if (skip < 0) skip = 0;

            var q = _db.PaymentReconciliationItems
                .AsNoTracking()
                .Where(i => i.CreatedAt >= fromUtc && i.CreatedAt <= toUtc);

            if (provider.HasValue)
                q = q.Where(i => i.Provider == provider.Value);

            if (status.HasValue)
                q = q.Where(i => i.Status == status.Value);

            if (reason.HasValue)
                q = q.Where(i => i.Reason == reason.Value);

            var total = await q.CountAsync(ct);

            // Summary counts by status
            var grouped = await q
                .GroupBy(x => x.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int Count(ReconciliationStatus s) => grouped.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

            // Rows
            var rows = await q
                .OrderByDescending(x => x.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(x => new ReconciliationReportRow
                {
                    ItemId = x.Id,
                    RunId = x.RunId,
                    CreatedAt = x.CreatedAt,
                    Provider = x.Provider,
                    Reference = x.Reference,
                    Status = x.Status,
                    Reason = x.Reason,
                    Details = x.Details,
                    PaymentIntentId = x.PaymentIntentId,
                    ProviderTransactionRowId = x.ProviderTransactionIdRef,
                    InvoiceId = x.InvoiceId,

                    IntentAmount = x.PaymentIntent != null ? x.PaymentIntent.Amount : null,
                    IntentCurrency = x.PaymentIntent != null ? x.PaymentIntent.Currency : null,
                    IntentStatus = x.PaymentIntent != null ? x.PaymentIntent.Status : null,
                    IntentFinalized = x.PaymentIntent != null ? x.PaymentIntent.IsFinalized : null,
                    Purpose = x.PaymentIntent != null ? x.PaymentIntent.Purpose : null,
                    InstitutionId = x.PaymentIntent != null ? x.PaymentIntent.InstitutionId : null,
                    UserId = x.PaymentIntent != null ? x.PaymentIntent.UserId : null
                })
                .ToListAsync(ct);

            return new ReconciliationReportResponse
            {
                Total = total,
                Matched = Count(ReconciliationStatus.Matched),
                NeedsReview = Count(ReconciliationStatus.NeedsReview),
                Mismatch = Count(ReconciliationStatus.Mismatch),
                MissingInternalIntent = Count(ReconciliationStatus.MissingInternalIntent),
                MissingProviderTransaction = Count(ReconciliationStatus.MissingProviderTransaction),
                Duplicate = Count(ReconciliationStatus.Duplicate),
                FinalizerFailed = Count(ReconciliationStatus.FinalizerFailed),
                ManuallyResolved = Count(ReconciliationStatus.ManuallyResolved),
                Items = rows
            };
        }

        private static ReconciliationRunResponse BuildRunResponse(PaymentReconciliationRun run, List<PaymentReconciliationItem> items)
        {
            int C(ReconciliationStatus s) => items.Count(x => x.Status == s);

            return new ReconciliationRunResponse
            {
                RunId = run.Id,
                FromUtc = run.FromUtc,
                ToUtc = run.ToUtc,
                Provider = run.Provider,
                TotalItems = items.Count,
                Matched = C(ReconciliationStatus.Matched),
                NeedsReview = C(ReconciliationStatus.NeedsReview),
                Mismatch = C(ReconciliationStatus.Mismatch),
                MissingInternalIntent = C(ReconciliationStatus.MissingInternalIntent),
                MissingProviderTransaction = C(ReconciliationStatus.MissingProviderTransaction),
                Duplicate = C(ReconciliationStatus.Duplicate),
                FinalizerFailed = C(ReconciliationStatus.FinalizerFailed),
                ManuallyResolved = C(ReconciliationStatus.ManuallyResolved)
            };
        }

        private async Task EnsureInvoiceForIntentAsync(PaymentIntent intent, CancellationToken ct)
        {
            if (intent.InvoiceId.HasValue && intent.InvoiceId.Value > 0)
                return;

            if (intent.Status != PaymentStatus.Success)
                return;

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
                IssuedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            invoice.Lines.Add(new InvoiceLine
            {
                Description = intent.Purpose.ToString(),
                ItemCode = "PAYMENT",
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
    }
}
