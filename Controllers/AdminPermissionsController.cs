using LawAfrica.API.Data;
using LawAfrica.API.Models.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/admin/permissions")]
    [Authorize(Roles = "Admin")]
    public class AdminPermissionsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AdminPermissionsController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost("seed")]
        public async Task<IActionResult> Seed()
        {
            var defaults = new List<AdminPermission>
            {
                new AdminPermission { Code = "users.create", Description = "Create users" },
                new AdminPermission { Code = "users.approve", Description = "Approve users" },
                new AdminPermission { Code = "records.delete", Description = "Delete records" },
                new AdminPermission { Code = "payments.reconcile", Description = "Reconcile payments" },
            };

            foreach (var p in defaults)
            {
                var exists = await _db.AdminPermissions.AnyAsync(x => x.Code == p.Code);
                if (!exists) _db.AdminPermissions.Add(p);
            }

            await _db.SaveChangesAsync();
            return Ok(new { message = "Permissions seeded." });
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var rows = await _db.AdminPermissions
                .AsNoTracking()
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Description, x.IsActive })
                .ToListAsync();

            return Ok(rows);
        }

        public class AssignRequest
        {
            public int UserId { get; set; }
            public string PermissionCode { get; set; } = string.Empty;
        }

        [HttpPost("assign")]
        public async Task<IActionResult> Assign([FromBody] AssignRequest req)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId);
            if (user == null) return BadRequest("User not found.");

            if (!string.Equals(user.Role, "Admin", StringComparison.OrdinalIgnoreCase))
                return BadRequest("User is not an Admin.");

            var perm = await _db.AdminPermissions.FirstOrDefaultAsync(p => p.Code == req.PermissionCode);
            if (perm == null) return BadRequest("Permission not found.");

            var exists = await _db.UserAdminPermissions.AnyAsync(x => x.UserId == req.UserId && x.PermissionId == perm.Id);
            if (!exists)
            {
                _db.UserAdminPermissions.Add(new UserAdminPermission
                {
                    UserId = req.UserId,
                    PermissionId = perm.Id,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            return Ok(new { message = "Assigned." });
        }
    }
}
