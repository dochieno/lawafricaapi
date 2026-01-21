using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using LawAfrica.API.Models.Institutions;
using LawAfrica.API.Services.Institutions;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services
{
    public class RegistrationService
    {
        private readonly ApplicationDbContext _db;
        private readonly AuthService _authService;
        private readonly InstitutionSeatGuard _seatGuard;

        public RegistrationService(
            ApplicationDbContext db,
            AuthService authService,
            InstitutionSeatGuard seatGuard)
        {
            _db = db;
            _authService = authService;
            _seatGuard = seatGuard;
        }

        public async Task<(User user, TwoFactorSetupResponse twoFactor)> CreateUserFromIntentAsync(
            RegistrationIntent intent)
        {
            var hasOuterTransaction = _db.Database.CurrentTransaction != null;

            await using var tx = hasOuterTransaction
                ? null
                : await _db.Database.BeginTransactionAsync();

            try
            {
                var dbIntent = await _db.RegistrationIntents
                    .FirstOrDefaultAsync(r => r.Id == intent.Id);

                if (dbIntent == null)
                    throw new InvalidOperationException("Registration intent not found (maybe already completed).");

                if (dbIntent.ExpiresAt < DateTime.UtcNow)
                    throw new InvalidOperationException("Registration intent has expired.");

                if (dbIntent.UserType == UserType.Public && !dbIntent.PaymentCompleted)
                    throw new InvalidOperationException("Payment not completed.");

                if (await _db.Users.AnyAsync(u => u.Email == dbIntent.Email))
                    throw new InvalidOperationException("An account with this email already exists.");

                if (await _db.Users.AnyAsync(u => u.Username == dbIntent.Username))
                    throw new InvalidOperationException("An account with this username already exists.");

                bool isApproved = true;

                Institution? institution = null;
                if (dbIntent.InstitutionId.HasValue)
                {
                    institution = await _db.Institutions
                        .FirstOrDefaultAsync(i => i.Id == dbIntent.InstitutionId);

                    if (institution == null || !institution.IsActive)
                        throw new InvalidOperationException("Institution is invalid or inactive.");

                    if (institution.RequiresUserApproval)
                        isApproved = false;
                }

                // ✅ Normalize all possibly-null strings ONCE
                var username = (dbIntent.Username ?? "").Trim();
                var email = (dbIntent.Email ?? "").Trim();
                var passwordHash = dbIntent.PasswordHash ?? "";

                var firstName = (dbIntent.FirstName ?? "").Trim();
                var lastName = (dbIntent.LastName ?? "").Trim();
                var phoneNumber = (dbIntent.PhoneNumber ?? "").Trim();

                // ✅ ReferenceNumber: normalize nullable ("" => null)
                var referenceNumber = string.IsNullOrWhiteSpace(dbIntent.ReferenceNumber)
                    ? null
                    : dbIntent.ReferenceNumber.Trim();

                // ✅ Require ReferenceNumber ONLY for institution users (Student/Staff signups)
                if (institution != null)
                {
                    if (string.IsNullOrWhiteSpace(referenceNumber))
                        throw new InvalidOperationException("Reference number is required for institution users.");

                    if (referenceNumber.Length < 3)
                        throw new InvalidOperationException("Reference number looks too short.");
                }
                else
                {
                    // Public users: never store empty-string reference numbers
                    referenceNumber = null;
                }

                var user = new User
                {
                    Username = username,
                    Email = email,
                    PasswordHash = passwordHash,

                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = phoneNumber,
                    CountryId = dbIntent.CountryId,

                    UserType = dbIntent.UserType,
                    InstitutionId = dbIntent.InstitutionId,

                    IsActive = true,
                    IsApproved = isApproved,

                    IsEmailVerified = false,

                    TwoFactorEnabled = false,
                    TwoFactorSecret = null,

                    CreatedAt = DateTime.UtcNow
                };

                _db.Users.Add(user);
                await _db.SaveChangesAsync();

                // ✅ Institution membership creation
                if (institution != null)
                {
                    var pending = institution.RequiresUserApproval;
                    var memberType = dbIntent.InstitutionMemberType ?? InstitutionMemberType.Student;

                    // Seat enforcement only if approval is immediate
                    if (!pending)
                    {
                        await _seatGuard.EnsureCanConsumeSeatForNewMembershipAsync(
                            institution.Id,
                            memberType);
                    }

                    var membership = new InstitutionMembership
                    {
                        InstitutionId = institution.Id,
                        UserId = user.Id,
                        MemberType = memberType,

                        // ✅ Save reference number (nullable in DB, but required for institution path)
                        ReferenceNumber = referenceNumber ?? string.Empty,

                        Status = pending
                            ? MembershipStatus.PendingApproval
                            : MembershipStatus.Approved,

                        IsActive = !pending,
                        CreatedAt = DateTime.UtcNow
                    };

                    _db.InstitutionMemberships.Add(membership);
                    await _db.SaveChangesAsync();
                }

                var setup = await _authService.EnableTwoFactorAuthAsync(user.Id);

                try
                {
                    await _authService.SendEmailVerificationAsync(user.Id);
                }
                catch
                {
                    // ignore verification email failures
                }

                _db.RegistrationIntents.Remove(dbIntent);
                await _db.SaveChangesAsync();

                if (tx != null)
                    await tx.CommitAsync();

                return (user, setup);
            }
            catch
            {
                if (tx != null)
                    await tx.RollbackAsync();

                _db.ChangeTracker.Clear();
                throw;
            }
        }

    }
}
