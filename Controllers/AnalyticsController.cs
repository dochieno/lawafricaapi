using LawAfrica.API.Data;
using LawAfrica.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/analytics")]
    [Authorize(Roles = "Admin")]
    public class AnalyticsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public AnalyticsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ------------------------------------------
        // GET: /api/admin/analytics/overview
        // ------------------------------------------
        [HttpGet("overview")]
        public async Task<IActionResult> Overview()
        {
            var totalDocs = await _db.LegalDocuments.CountAsync();
            var totalReaders = await _db.LegalDocumentProgress
                .Select(p => p.UserId)
                .Distinct()
                .CountAsync();

            var totalAnnotations = await _db.LegalDocumentAnnotations.CountAsync();
            var totalReadingTime = await _db.LegalDocumentProgress
                .SumAsync(p => p.TotalSecondsRead);

            return Ok(new PlatformAnalyticsDto(
                totalDocs,
                totalReaders,
                totalAnnotations,
                totalReadingTime
            ));
        }
    }
}
