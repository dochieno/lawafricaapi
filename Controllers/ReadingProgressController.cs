using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/reading-progress")]
    [Authorize]
    public class ReadingProgressController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public ReadingProgressController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ----------------------------------------------------
        // GET: /api/reading-progress/{documentId}
        // ----------------------------------------------------
        [HttpGet("{documentId:int}")]
        public async Task<ActionResult<ReadingProgressResponse>> Get(int documentId)
        {
            var userId = User.GetUserId();

            var progress = await _db.LegalDocumentProgress
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == documentId);

            if (progress == null)
            {
                // No progress yet → return defaults
                return Ok(new ReadingProgressResponse(
                    documentId,
                    null,
                    null,
                    null,
                    0,
                    false,
                    0,
                    DateTime.UtcNow
                ));
            }

            return Ok(Map(progress));
        }

        // ----------------------------------------------------
        // PUT: /api/reading-progress/{documentId}
        // ----------------------------------------------------
        [HttpPut("{documentId:int}")]
        public async Task<ActionResult<ReadingProgressResponse>> Upsert(
            int documentId,
            UpdateReadingProgressRequest request)
        {
            var userId = User.GetUserId();

            var documentExists = await _db.LegalDocuments
                .AnyAsync(d => d.Id == documentId);

            if (!documentExists)
                return NotFound("Legal document not found.");

            var progress = await _db.LegalDocumentProgress
                .FirstOrDefaultAsync(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == documentId);

            if (progress == null)
            {
                progress = new LegalDocumentProgress
                {
                    UserId = userId,
                    LegalDocumentId = documentId
                };

                _db.LegalDocumentProgress.Add(progress);
            }

            // ---------------- POSITION ----------------
            progress.PageNumber = request.PageNumber ?? progress.PageNumber;
            progress.Cfi = request.Cfi ?? progress.Cfi;
            progress.CharOffset = request.CharOffset ?? progress.CharOffset;

            // ---------------- PROGRESS ----------------
            var percentage = Math.Clamp(request.Percentage, 0, 100);
            progress.Percentage = percentage;

            if (request.IsCompleted == true || percentage >= 99.5)
                progress.IsCompleted = true;

            // ---------------- TIME ----------------
            var delta = Math.Clamp(request.SecondsReadDelta, 0, 3600);
            progress.TotalSecondsRead += delta;

            // ---------------- AUDIT ----------------
            progress.LastReadAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(Map(progress));
        }

        // ----------------------------------------------------
        // GET: /api/reading-progress/recent?take=10
        // ----------------------------------------------------
        [HttpGet("recent")]
        public async Task<ActionResult<List<ReadingProgressResponse>>> Recent([FromQuery] int take = 10)
        {
            var userId = User.GetUserId();

            var items = await _db.LegalDocumentProgress
                .AsNoTracking()
                .Where(x => x.UserId == userId)
                .OrderByDescending(x => x.LastReadAt)
                .Take(Math.Clamp(take, 1, 50))
                .ToListAsync();

            return Ok(items.Select(Map));
        }

        // ----------------------------------------------------
        private static ReadingProgressResponse Map(LegalDocumentProgress p)
        {
            return new ReadingProgressResponse(
                p.LegalDocumentId,
                p.PageNumber,
                p.Cfi,
                p.CharOffset,
                p.Percentage,
                p.IsCompleted,
                p.TotalSecondsRead,
                p.LastReadAt
            );
        }
    }
}
