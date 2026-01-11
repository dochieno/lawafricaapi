using System.Text.RegularExpressions;
using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Centralized business validation for payment flows.
    /// NOT authorization. Authorization is handled by Policies/Handlers.
    ///
    /// Goal:
    /// - Keep controllers thin
    /// - Ensure consistent rules across Mpesa and manual approvals
    /// - Fail fast with clear messages
    /// </summary>
    public class PaymentValidationService
    {
        private readonly ApplicationDbContext _db;

        public PaymentValidationService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Validates the STK initiate request for correctness + existence checks.
        /// Throws InvalidOperationException with a clear message if invalid.
        ///
        /// NOTE: Controllers should convert exceptions to BadRequest.
        /// </summary>
        public async Task ValidateStkInitiateAsync(
            PaymentPurpose purpose,
            decimal amount,
            string? phoneNumber,
            int? registrationIntentId,
            int? contentProductId,
            int? institutionId,
            int? durationInMonths,
            int? legalDocumentId = null) // ✅ NEW (optional, only used for PublicLegalDocumentPurchase)
        {
            // -------------------------------
            // Basic validation (fail fast)
            // -------------------------------
            if (amount <= 0)
                throw new InvalidOperationException("Amount must be greater than 0.");

            if (string.IsNullOrWhiteSpace(phoneNumber))
                throw new InvalidOperationException("PhoneNumber is required.");

            phoneNumber = phoneNumber.Trim();

            // Sandbox + production both prefer 2547XXXXXXXX (12 digits)
            if (!Regex.IsMatch(phoneNumber, @"^2547\d{8}$"))
                throw new InvalidOperationException("PhoneNumber must be in format 2547XXXXXXXX (12 digits).");

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

                // ✅ NEW PURPOSE: Public legal document purchase (one-off)
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

            // Signup fee requires registration intent
            if (purpose == PaymentPurpose.PublicSignupFee)
            {
                var intentExists = await _db.RegistrationIntents
                    .AnyAsync(r => r.Id == registrationIntentId!.Value);

                if (!intentExists)
                    throw new InvalidOperationException("Registration intent not found.");
            }

            // Purchase/subscription require product existence
            if (purpose == PaymentPurpose.PublicProductPurchase ||
                purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                var productExists = await _db.ContentProducts
                    .AnyAsync(p => p.Id == contentProductId!.Value);

                if (!productExists)
                    throw new InvalidOperationException("Content product not found.");
            }

            // Subscription requires institution existence + active
            if (purpose == PaymentPurpose.InstitutionProductSubscription)
            {
                var institutionExists = await _db.Institutions
                    .AnyAsync(i => i.Id == institutionId!.Value && i.IsActive);

                if (!institutionExists)
                    throw new InvalidOperationException("Institution not found or inactive.");
            }

            // ✅ NEW: legal document existence + purchasable checks
            if (purpose == PaymentPurpose.PublicLegalDocumentPurchase)
            {
                var doc = await _db.LegalDocuments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == legalDocumentId!.Value);

                if (doc == null)
                    throw new InvalidOperationException("Legal document not found.");

                if (doc.Status != LegalDocumentStatus.Published)
                    throw new InvalidOperationException("Legal document is not published.");

                if (!doc.AllowPublicPurchase || doc.PublicPrice == null || doc.PublicPrice <= 0)
                    throw new InvalidOperationException("This document is not available for individual purchase.");

                // Optional strict amount match (recommended)
                // If you want to allow discounts later, remove this.
                if (doc.PublicPrice.Value != amount)
                    throw new InvalidOperationException("Amount does not match current document price.");
            }
        }

        /// <summary>
        /// Validates manual (offline) institution subscription payment creation.
        /// Throws InvalidOperationException if invalid.
        /// </summary>
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
    }
}
