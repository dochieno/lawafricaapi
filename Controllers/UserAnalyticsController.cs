using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/analytics/user")]
    [Authorize]
    public class UserAnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public UserAnalyticsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ------------------------------------------
        // GET: /api/analytics/user/overview
        // ------------------------------------------
        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            var userId = User.GetUserId();

            var progress = await _db.LegalDocumentProgress
                .Where(p => p.UserId == userId)
                .ToListAsync();

            var totalTime = progress.Sum(p => p.TotalSecondsRead);
            var completed = progress.Count(p => p.IsCompleted);

            // Simple streak: count distinct days with activity
            var streak = progress
                .Select(p => p.LastReadAt.Date)
                .Distinct()
                .OrderByDescending(d => d)
                .TakeWhile((date, index) =>
                    date == DateTime.UtcNow.Date.AddDays(-index))
                .Count();

            return Ok(new UserReadingOverviewDto(
                progress.Count,
                completed,
                totalTime,
                streak
            ));
        }

        // ------------------------------------------
        // GET: /api/analytics/user/documents
        // ------------------------------------------
        [HttpGet("documents")]
        public async Task<IActionResult> Documents()
        {
            var userId = User.GetUserId();

            var data = await _db.LegalDocumentProgress
                .Include(p => p.LegalDocument)
                .Where(p => p.UserId == userId)
                .OrderByDescending(p => p.LastReadAt)
                .Select(p => new UserDocumentAnalyticsDto(
                    p.LegalDocumentId,
                    p.LegalDocument.Title,
                    p.Percentage,
                    p.TotalSecondsRead,
                    p.IsCompleted,
                    p.LastReadAt
                ))
                .ToListAsync();

            return Ok(data);
        }
    }
}
