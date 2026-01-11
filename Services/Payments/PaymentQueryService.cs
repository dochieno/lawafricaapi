using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs.Payments;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Payments
{
    /// <summary>
    /// Read-only queries for payment history and audits.
    /// No mutation logic allowed here.
    /// </summary>
    public class PaymentQueryService
    {
        private readonly ApplicationDbContext _db;

        public PaymentQueryService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<PaymentHistoryItemDto>> GetUserPaymentsAsync(int userId)
        {
            return await _db.PaymentIntents
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PaymentHistoryItemDto
                {
                    PaymentIntentId = p.Id,
                    Purpose = p.Purpose,
                    Method = p.Method,
                    Status = p.Status,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    ProviderReference = p.MpesaReceiptNumber ?? p.ManualReference,
                    CreatedAt = p.CreatedAt,
                    ContentProductId = p.ContentProductId,
                    InstitutionId = p.InstitutionId,
                    IsFinalized = p.IsFinalized,
                    ApprovedByUserId = p.ApprovedByUserId,
                    ApprovedAt = p.ApprovedAt
                })
                .ToListAsync();
        }

        public async Task<List<PaymentHistoryItemDto>> GetInstitutionPaymentsAsync(int institutionId)
        {
            return await _db.PaymentIntents
                .Where(p => p.InstitutionId == institutionId)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new PaymentHistoryItemDto
                {
                    PaymentIntentId = p.Id,
                    Purpose = p.Purpose,
                    Method = p.Method,
                    Status = p.Status,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    ProviderReference = p.MpesaReceiptNumber ?? p.ManualReference,
                    CreatedAt = p.CreatedAt,
                    ContentProductId = p.ContentProductId,
                    InstitutionId = p.InstitutionId,
                    IsFinalized = p.IsFinalized,
                    ApprovedByUserId = p.ApprovedByUserId,
                    ApprovedAt = p.ApprovedAt
                })
                .ToListAsync();
        }

        public async Task<PaymentDetailDto?> GetPaymentDetailAsync(int paymentIntentId)
        {
            return await _db.PaymentIntents
                .Where(p => p.Id == paymentIntentId)
                .Select(p => new PaymentDetailDto
                {
                    PaymentIntentId = p.Id,
                    Purpose = p.Purpose,
                    Method = p.Method,
                    Status = p.Status,
                    Amount = p.Amount,
                    Currency = p.Currency,
                    PhoneNumber = p.PhoneNumber,
                    ProviderResultCode = p.ProviderResultCode,
                    ProviderResultDesc = p.ProviderResultDesc,
                    ProviderReference = p.MpesaReceiptNumber ?? p.ManualReference,
                    UserId = p.UserId,
                    InstitutionId = p.InstitutionId,
                    ContentProductId = p.ContentProductId,
                    IsFinalized = p.IsFinalized,
                    AdminNotes = p.AdminNotes,
                    ApprovedByUserId = p.ApprovedByUserId,
                    ApprovedAt = p.ApprovedAt,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt
                })
                .FirstOrDefaultAsync();
        }
    }
}
