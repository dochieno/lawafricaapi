using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs.Institutions;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Services.Institutions
{
    public class InstitutionService
    {
        private readonly ApplicationDbContext _db;
        private readonly InstitutionRegistrationNumberGenerator _regNoGen;
        private readonly InstitutionAccessCodeGenerator _accessCodeGen;

        public InstitutionService(
            ApplicationDbContext db,
            InstitutionRegistrationNumberGenerator regNoGen,
            InstitutionAccessCodeGenerator accessCodeGen)
        {
            _db = db;
            _regNoGen = regNoGen;
            _accessCodeGen = accessCodeGen;
        }

        public async Task<List<InstitutionListItemDto>> GetAllAsync(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var query = _db.Institutions.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(i =>
                    i.Name.ToLower().Contains(q) ||
                    (i.ShortName != null && i.ShortName.ToLower().Contains(q)) ||
                    i.EmailDomain.ToLower().Contains(q) ||
                    i.OfficialEmail.ToLower().Contains(q));
            }

            return await query
                .OrderByDescending(i => i.CreatedAt)
                .Select(i => new InstitutionListItemDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    ShortName = i.ShortName,
                    EmailDomain = i.EmailDomain,
                    OfficialEmail = i.OfficialEmail,
                    InstitutionType = i.InstitutionType,
                    RequiresUserApproval = i.RequiresUserApproval,
                    IsVerified = i.IsVerified,
                    IsActive = i.IsActive,
                    CreatedAt = i.CreatedAt,
                    AllowIndividualPurchasesWhenInstitutionInactive=i.AllowIndividualPurchasesWhenInstitutionInactive

                })
                .ToListAsync();
        }

        public async Task<InstitutionDetailsDto?> GetByIdAsync(int id)
        {
            return await _db.Institutions
                .AsNoTracking()
                .Include(i => i.Country)
                .Where(i => i.Id == id)
                .Select(i => new InstitutionDetailsDto
                {
                    Id = i.Id,
                    Name = i.Name,
                    ShortName = i.ShortName,
                    EmailDomain = i.EmailDomain,
                    OfficialEmail = i.OfficialEmail,

                    PhoneNumber = i.PhoneNumber,
                    AlternatePhoneNumber = i.AlternatePhoneNumber,
                    AddressLine1 = i.AddressLine1,
                    AddressLine2 = i.AddressLine2,
                    City = i.City,
                    StateOrProvince = i.StateOrProvince,
                    PostalCode = i.PostalCode,

                    CountryId = i.CountryId,
                    CountryName = i.Country != null ? i.Country.Name : null,

                    RegistrationNumber = i.RegistrationNumber,
                    TaxPin = i.TaxPin,

                    InstitutionType = i.InstitutionType,
                    InstitutionAccessCode = i.InstitutionAccessCode,
                    RequiresUserApproval = i.RequiresUserApproval,
                    MaxStudentSeats = i.MaxStudentSeats,
                    MaxStaffSeats = i.MaxStaffSeats,

                    IsVerified = i.IsVerified,
                    IsActive = i.IsActive,
                    ActivatedAt = i.ActivatedAt,

                    CreatedAt = i.CreatedAt,
                    UpdatedAt = i.UpdatedAt,
                    AllowIndividualPurchasesWhenInstitutionInactive = i.AllowIndividualPurchasesWhenInstitutionInactive
                })
                .FirstOrDefaultAsync();
        }

            public async Task<int> CreateAsync(CreateInstitutionRequest req)
            {
                Validate(req);

                var emailDomain = NormalizeDomain(req.EmailDomain);

                var exists = await _db.Institutions.AnyAsync(i =>
                    i.EmailDomain.ToLower() == emailDomain.ToLower());

                if (exists)
                    throw new InvalidOperationException("An institution with this email domain already exists.");

                var entity = new Institution
                {
                    Name = req.Name.Trim(),
                    ShortName = string.IsNullOrWhiteSpace(req.ShortName) ? null : req.ShortName.Trim(),

                    EmailDomain = emailDomain,
                    OfficialEmail = req.OfficialEmail.Trim(),

                    PhoneNumber = NullIfEmpty(req.PhoneNumber),
                    AlternatePhoneNumber = NullIfEmpty(req.AlternatePhoneNumber),

                    AddressLine1 = NullIfEmpty(req.AddressLine1),
                    AddressLine2 = NullIfEmpty(req.AddressLine2),
                    City = NullIfEmpty(req.City),
                    StateOrProvince = NullIfEmpty(req.StateOrProvince),
                    PostalCode = NullIfEmpty(req.PostalCode),

                    CountryId = req.CountryId,

                    RegistrationNumber = NullIfEmpty(req.RegistrationNumber),
                    TaxPin = NullIfEmpty(req.TaxPin),

                    InstitutionType = req.InstitutionType,

                    // NOTE: we'll normalize below and ensure it is always set
                    InstitutionAccessCode = req.InstitutionAccessCode,

                    RequiresUserApproval = req.RequiresUserApproval,
                    MaxStudentSeats = req.MaxStudentSeats,
                    MaxStaffSeats = req.MaxStaffSeats,

                    IsVerified = false,
                    IsActive = false,
                    ActivatedAt = null,

                    AllowIndividualPurchasesWhenInstitutionInactive = false,

                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = null
                };

                if (string.IsNullOrWhiteSpace(entity.RegistrationNumber))
                    entity.RegistrationNumber = await _regNoGen.GenerateNextAsync();

                // ✅ Always store a normalized code (never null/empty)
                static string NormalizeCode(string s) => s.Trim().ToUpperInvariant();

                if (!string.IsNullOrWhiteSpace(entity.InstitutionAccessCode))
                    entity.InstitutionAccessCode = NormalizeCode(entity.InstitutionAccessCode);

                _db.Institutions.Add(entity);

                // ✅ Retry a few times if we hit UNIQUE collisions for RegistrationNumber or InstitutionAccessCode
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    // Ensure code exists before every save attempt (important if previous attempt failed)
                    if (string.IsNullOrWhiteSpace(entity.InstitutionAccessCode))
                        entity.InstitutionAccessCode = NormalizeCode(await _accessCodeGen.GenerateUniqueAsync());

                    if (string.IsNullOrWhiteSpace(entity.RegistrationNumber))
                        entity.RegistrationNumber = await _regNoGen.GenerateNextAsync();

                    try
                    {
                        await _db.SaveChangesAsync();
                        return entity.Id;
                    }
                    catch (DbUpdateException)
                    {
                        // We don’t know if it was RegistrationNumber or AccessCode (or something else),
                        // so regenerate both safely and retry.
                        entity.InstitutionAccessCode = NormalizeCode(await _accessCodeGen.GenerateUniqueAsync());
                        entity.RegistrationNumber = await _regNoGen.GenerateNextAsync();

                        // Important: do NOT call SaveChanges here; loop will retry cleanly.
                    }
                }

                // If we got here, something else is wrong or collisions are extreme
                throw new InvalidOperationException(
                    "Failed to create institution after multiple attempts. Please try again.");
            }

        public async Task UpdateAsync(int id, UpdateInstitutionRequest req)
        {
            Validate(req);

            var entity = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == id);
            if (entity == null)
                throw new KeyNotFoundException("Institution not found.");

            var newDomain = NormalizeDomain(req.EmailDomain);

            if (!string.Equals(entity.EmailDomain, newDomain, StringComparison.OrdinalIgnoreCase))
            {
                var exists = await _db.Institutions.AnyAsync(i =>
                    i.Id != id && i.EmailDomain.ToLower() == newDomain.ToLower());

                if (exists)
                    throw new InvalidOperationException("Another institution already uses this email domain.");
            }

            entity.Name = req.Name.Trim();
            entity.ShortName = string.IsNullOrWhiteSpace(req.ShortName) ? null : req.ShortName.Trim();

            entity.EmailDomain = newDomain;
            entity.OfficialEmail = req.OfficialEmail.Trim();

            entity.PhoneNumber = NullIfEmpty(req.PhoneNumber);
            entity.AlternatePhoneNumber = NullIfEmpty(req.AlternatePhoneNumber);

            entity.AddressLine1 = NullIfEmpty(req.AddressLine1);
            entity.AddressLine2 = NullIfEmpty(req.AddressLine2);
            entity.City = NullIfEmpty(req.City);
            entity.StateOrProvince = NullIfEmpty(req.StateOrProvince);
            entity.PostalCode = NullIfEmpty(req.PostalCode);

            entity.CountryId = req.CountryId;

            entity.RegistrationNumber = NullIfEmpty(req.RegistrationNumber);
            entity.TaxPin = NullIfEmpty(req.TaxPin);

            entity.InstitutionType = req.InstitutionType;
            entity.InstitutionAccessCode = NullIfEmpty(req.InstitutionAccessCode);
            entity.RequiresUserApproval = req.RequiresUserApproval;
            entity.MaxStudentSeats = req.MaxStudentSeats;
            entity.MaxStaffSeats = req.MaxStaffSeats;

            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task SetActiveAsync(int id, bool active)
        {
            var entity = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == id);
            if (entity == null)
                throw new KeyNotFoundException("Institution not found.");

            entity.IsActive = active;
            entity.ActivatedAt = active ? (entity.ActivatedAt ?? DateTime.UtcNow) : null;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        public async Task SetVerifiedAsync(int id, bool verified)
        {
            var entity = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == id);
            if (entity == null)
                throw new KeyNotFoundException("Institution not found.");

            entity.IsVerified = verified;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        // ✅ NEW: called by InstitutionsController POST /{id}/purchase-policy
        public async Task SetAllowIndividualPurchasesWhenInstitutionInactiveAsync(int institutionId, bool allowed)
        {
            var entity = await _db.Institutions.FirstOrDefaultAsync(i => i.Id == institutionId);
            if (entity == null)
                throw new KeyNotFoundException("Institution not found.");

            entity.AllowIndividualPurchasesWhenInstitutionInactive = allowed;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
        }

        private static void Validate(CreateInstitutionRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                throw new InvalidOperationException("Name is required.");

            if (string.IsNullOrWhiteSpace(req.EmailDomain))
                throw new InvalidOperationException("Email domain is required.");

            if (string.IsNullOrWhiteSpace(req.OfficialEmail))
                throw new InvalidOperationException("Official email is required.");
        }

        private static string NormalizeDomain(string domain)
        {
            domain = (domain ?? "").Trim().ToLowerInvariant();
            domain = domain.Replace("@", "");
            return domain;
        }

        private static string? NullIfEmpty(string? s)
        {
            var t = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }

        public async Task<List<object>> GetPublicAsync(string? q)
        {
            q = (q ?? "").Trim().ToLower();

            var query = _db.Institutions.AsNoTracking().Where(i => i.IsActive);

            if (!string.IsNullOrWhiteSpace(q))
            {
                query = query.Where(i =>
                    i.Name.ToLower().Contains(q) ||
                    (i.ShortName != null && i.ShortName.ToLower().Contains(q)) ||
                    i.EmailDomain.ToLower().Contains(q));
            }

            return await query
                .OrderBy(i => i.Name)
                .Select(i => new
                {
                    i.Id,
                    i.Name,
                    i.ShortName,
                    i.EmailDomain,
                    i.InstitutionType,
                    i.RequiresUserApproval,
                    AccessCodeRequired = i.InstitutionAccessCode != null && i.InstitutionAccessCode.Trim() != ""
                })
                .Cast<object>()
                .ToListAsync();
        }

        public async Task<List<InstitutionUserDto>> GetUsersAsync(int institutionId)
        {
            var exists = await _db.Institutions
                .AsNoTracking()
                .AnyAsync(i => i.Id == institutionId);

            if (!exists)
                throw new InvalidOperationException("Institution not found.");

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => u.InstitutionId == institutionId)
                .OrderByDescending(u => u.CreatedAt)
                .Select(u => new InstitutionUserDto
                {
                    Id = u.Id,
                    Username = u.Username ?? "",
                    Email = u.Email ?? "",
                    Role = u.Role ?? "User",
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt,
                    FirstName = u.FirstName,
                    LastName = u.LastName
                })
                .ToListAsync();

            return users;
        }

        public async Task<Institution?> GetEntityAsync(int id)
        {
            return await _db.Institutions.FirstOrDefaultAsync(i => i.Id == id);
        }

        public async Task SaveAsync(Institution institution)
        {
            institution.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

    }
}
