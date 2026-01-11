using LawAfrica.API.Data;
using LawAfrica.API.Models;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Data.Seed
{
    public static class AdminUserSeeder
    {
        public static async Task SeedAsync(ApplicationDbContext db)
        {
            // 🔒 Prevent duplicate admin
            var exists = await db.Users.AnyAsync(u => u.IsGlobalAdmin);
            if (exists) return;

            // 🔐 Hash password (must match your auth logic)
            var passwordHash = BCrypt.Net.BCrypt.HashPassword("Open123!");

            var admin = new User
            {
                Username = "dochieno",
                Email = "ochienooduory@outlook.com",
                PhoneNumber = "0720726258",
                PasswordHash = passwordHash,

                UserType = UserType.Admin,
                Role = "Admin",
                IsGlobalAdmin = true,

                IsActive = true,
                IsApproved = true,
                IsEmailVerified = true,
                TwoFactorEnabled = false,
                FailedLoginAttempts = 0,

                CreatedAt = DateTime.UtcNow
            };

            db.Users.Add(admin);
            await db.SaveChangesAsync();
        }
    }
}
