using System.Text.RegularExpressions;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    public class PaymentValidationService
    {
        private readonly ApplicationDbContext _db;

        public PaymentValidationService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task ValidateStkInitiateAsync(
            PaymentPurpose purpose,
            decimal amount,
            string? phoneNumber,
            int? registrationIntentId,
            int? contentProductId,
            int? institutionId,
            int? durationInMonths,
            int? legalDocumentId = null)
        {
            // -------------------------------
            // Basic validation (fail fast)
            // -------------------------------
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than 0.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new InvalidOperationException("PhoneNumber is required.");

            phoneNumber = phoneNumber.Trim().Replace(" ", "");

            try
            {
                phoneNumber = FormatToMpesaStandard(phoneNumber);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    "PhoneNumber must be in format 07XXXXXXXX, 01XXXXXXXX, 2547XXXXXXXX or 2541XXXXXXXX.",
                    ex
                );
            }

            // -------------------------------
            // Purpose-specific validation
            // -------------------------------
            switch (purpose)
            {
                case PaymentPurpose.PublicSignupFee:
                    if (!registrationIntentId.HasValue || registrationIntentId.Value <= 0)
                        throw new InvalidOperationException("RegistrationIntentId is required for PublicSignupFee.");

                    if (contentProductId.HasValue)
                        throw new InvalidOperationException("ContentProductId must be omitted for PublicSignupFee.");

                    if (institutionId.HasValue)
                        throw new InvalidOperationException("InstitutionId must be omitted for PublicSignupFee.");

                    if (durationInMonths.HasValue)
                        throw new InvalidOperationException("DurationInMonths must be omitted for PublicSignupFee.");

                    if (legalDocumentId.HasValue)
                        throw new InvalidOperationException("LegalDocumentId must be omitted for PublicSignupFee.");
                    break;

                case PaymentPurpose.PublicProductPurchase:
                    if (!contentProductId.HasValue || contentProductId.Value <= 0)
                        throw new InvalidOperationException("ContentProductId is required for PublicProductPurchase.");

                    if (registrationIntentId.HasValue)
                        throw new InvalidOperationException("RegistrationIntentId must be omitted for PublicProductPurchase.");

                    if (institutionId.HasValue)
                        throw new InvalidOperationException("InstitutionId must be omitted for PublicProductPurchase.");

                    if (durationInMonths.HasValue)
                        throw new InvalidOperationException("DurationInMonths must be omitted for PublicProductPurchase.");

                    if (legalDocumentId.HasValue)
                        throw new InvalidOperationException("LegalDocumentId must be omitted for PublicProductPurchase.");
                    break;

                // ✅ NEW: Public subscription
                case PaymentPurpose.PublicProductSubscription:
                    if (!contentProductId.HasValue || contentProductId.Value <= 0)
                        throw new InvalidOperationException("ContentProductId is required for PublicProductSubscription.");

                    if (registrationIntentId.HasValue)
                        throw new InvalidOperationException("RegistrationIntentId must be omitted for PublicProductSubscription.");

                    if (institutionId.HasValue)
                        throw new InvalidOperationException("InstitutionId must be omitted for PublicProductSubscription.");

                    if (!durationInMonths.HasValue || durationInMonths.Value <= 0)
                        throw new InvalidOperationException("DurationInMonths must be greater than 0 for PublicProductSubscription.");

                    if (legalDocumentId.HasValue)
                        throw new InvalidOperationException("LegalDocumentId must be omitted for PublicProductSubscription.");
                    break;

                case PaymentPurpose.InstitutionProductSubscription:
                    if (!institutionId.HasValue || institutionId.Value <= 0)
                        throw new InvalidOperationException("InstitutionId is required for InstitutionProductSubscription.");

                    if (!contentProductId.HasValue || contentProductId.Value <= 0)
                        throw new InvalidOperationException("ContentProductId is required for InstitutionProductSubscription.");

                    if (!durationInMonths.HasValue || durationInMonths.Value <= 0)
                        throw new InvalidOperationException("DurationInMonths must be greater than 0 for InstitutionProductSubscription.");

                    if (registrationIntentId.HasValue)
                        throw new InvalidOperationException("RegistrationIntentId must be omitted for InstitutionProductSubscription.");

                    if (legalDocumentId.HasValue)
                        throw new InvalidOperationException("LegalDocumentId must be omitted for InstitutionProductSubscription.");
                    break;

                case PaymentPurpose.PublicLegalDocumentPurchase:
                    if (!legalDocumentId.HasValue || legalDocumentId.Value <= 0)
                        throw new InvalidOperationException("LegalDocumentId is required for PublicLegalDocumentPurchase.");

                    if (registrationIntentId.HasValue)
                        throw new InvalidOperationException("RegistrationIntentId must be omitted for PublicLegalDocumentPurchase.");

                    if (contentProductId.HasValue)
                        throw new InvalidOperationException("ContentProductId must be omitted for PublicLegalDocumentPurchase.");

                    if (institutionId.HasValue)
                        throw new InvalidOperationException("InstitutionId must be omitted for PublicLegalDocumentPurchase.");

                    if (durationInMonths.HasValue)
                        throw new InvalidOperationException("DurationInMonths must be omitted for PublicLegalDocumentPurchase.");
                    break;

                default:
                    throw new InvalidOperationException("Invalid payment purpose.");
            }

            // -------------------------------
            // Existence validation (DB)
            // -------------------------------
            if (purpose == PaymentPurpose.PublicSignupFee)
            {
                var intentExists = await _db.RegistrationIntents
                    .AnyAsync(r => r.Id == registrationIntentId!.Value);

                if (!intentExists)
                    throw new InvalidOperationException("Registration intent not found.");
            }

            if (purpose == PaymentPurpose.PublicProductPurchase ||
                purpose == PaymentPurpose.PublicProductSubscription ||
                purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                var product = await _db.ContentProducts
                    .AsNoTracking()
                    .Where(p => p.Id == contentProductId!.Value)
                    .Select(p => new
                    {
                        p.Id,
                        p.AvailableToPublic,
                        p.AvailableToInstitutions,
                        p.PublicAccessModel,
                        p.InstitutionAccessModel
                    })
                    .FirstOrDefaultAsync();

                if (product == null)
                    throw new InvalidOperationException("Content product not found.");

                // ✅ Public subscription must be allowed & must be subscription-based
                if (purpose == PaymentPurpose.PublicProductSubscription)
                {
                    if (!product.AvailableToPublic)
                        throw new InvalidOperationException("This product is not available to public users.");

                    if (product.PublicAccessModel != ProductAccessModel.Subscription)
                        throw new InvalidOperationException("This product is not available as a public subscription.");
                }
            }

            if (purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                var institutionExists = await _db.Institutions
                    .AnyAsync(i => i.Id == institutionId!.Value && i.IsActive);

                if (!institutionExists)
                    throw new InvalidOperationException("Institution not found or inactive.");
            }

            // -------------------------------
            // ✅ Legal document checks (VAT-aware amount check)
            // -------------------------------
            if (purpose == PaymentPurpose.PublicLegalDocumentPurchase)
            {
                var doc = await _db.LegalDocuments
                    .AsNoTracking()
                    .Include(d => d.VatRate)
                    .FirstOrDefaultAsync(d => d.Id == legalDocumentId!.Value);

                if (doc == null)
                    throw new InvalidOperationException("Legal document not found.");

                if (doc.Status != LegalDocumentStatus.Published)
                    throw new InvalidOperationException("Legal document is not published.");

                if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
                    throw new InvalidOperationException("This document is not available for individual purchase.");

                var expectedGross = ComputeExpectedLegalDocGross(doc);
                var a = Round2(amount);
                var e = Round2(expectedGross);

                if (Math.Abs(a - e) > 0.01m)
                    throw new InvalidOperationException("Amount does not match current document price.");
            }
        }

        public async Task ValidateManualInstitutionSubscriptionAsync(
            int institutionId,
            int contentProductId,
            int durationInMonths,
            decimal amount,
            string? manualReference)
        {
            if (institutionId <= 0)
                throw new InvalidOperationException("InstitutionId is required.");

            if (contentProductId <= 0)
                throw new InvalidOperationException("ContentProductId is required.");

            if (durationInMonths <= 0)
                throw new InvalidOperationException("DurationInMonths must be greater than 0.");

            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than 0.");

            if (string.IsNullOrWhiteSpace(manualReference))
                throw new InvalidOperationException("ManualReference is required (bank/EFT reference).");

            var institutionExists = await _db.Institutions
                .AnyAsync(i => i.Id == institutionId && i.IsActive);

            if (!institutionExists)
                throw new InvalidOperationException("Institution not found or inactive.");

            var productExists = await _db.ContentProducts
                .AnyAsync(p => p.Id == contentProductId);

            if (!productExists)
                throw new InvalidOperationException("Content product not found.");
        }

        private static decimal ComputeExpectedLegalDocGross(LegalDocument doc)
        {
            var price = doc.PublicPrice ?? 0m;
            if (price <= 0m) return 0m;

            var rate = doc.VatRate != null ? doc.VatRate.RatePercent : 0m;

            if (rate <= 0m)
                return Round2(price);

            if (doc.IsTaxInclusive)
                return Round2(price);

            var vat = price * (rate / 100m);
            return Round2(price + vat);
        }

        private static decimal Round2(decimal v)
            => Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public static string FormatToMpesaStandard(string phoneNumber)
        {
            if (phoneNumber is null)
                throw new ArgumentNullException(nameof(phoneNumber), "Please provide a phone number to make this payment.");

            phoneNumber = phoneNumber.Trim();
            var match = Regex.Match(phoneNumber, @"^(?:254|\+254|0)?((?:7|1)[0-9]{8})$");
            if (!match.Success)
                throw new ArgumentException("Invalid phone number format. Expected a Kenyan mobile number (e.g., 07XXXXXXXX, 01XXXXXXXX, +2547XXXXXXXX).", nameof(phoneNumber));

            return "254" + match.Groups[1].Value;
        }
    }
}
