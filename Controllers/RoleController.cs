using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RoleController : ControllerBase
    {
        // Anyone logged in (User or Admin)
        [Authorize(Policy = "RequireUser")]
        [HttpGet("user-area")]
        public IActionResult UserArea()
        {
            return Ok(new
            {
                message = "You are authenticated as a User or Admin.",
                user = User.Identity?.Name,
                roles = User.Claims
                    .Where(c => c.Type.Contains("role"))
                    .Select(c => c.Value)
            });
        }

        // Only Admins
        [Authorize(Policy = "RequireAdmin")]
        [HttpGet("admin-area")]
        public IActionResult AdminArea()
        {
            return Ok(new
            {
                message = "You are authenticated as an Admin.",
                user = User.Identity?.Name,
                roles = User.Claims
                    .Where(c => c.Type.Contains("role"))
                    .Select(c => c.Value)
            });
        }
    }
}
