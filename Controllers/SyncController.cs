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
    [Route("api/sync")]
    [Authorize]
    public class SyncController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public SyncController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Sync(SyncRequest request)
        {
            var userId = User.GetUserId();
            var now = DateTime.UtcNow;

            // -------------------------
            // 1. APPLY PROGRESS UPDATES
            // -------------------------
            foreach (var item in request.ProgressUpdates)
            {
                var progress = await _db.LegalDocumentProgress
                    .FirstOrDefaultAsync(p =>
                        p.UserId == userId &&
                        p.LegalDocumentId == item.LegalDocumentId);

                if (progress == null)
                {
                    progress = new LegalDocumentProgress
                    {
                        UserId = userId,
                        LegalDocumentId = item.LegalDocumentId
                    };
                    _db.LegalDocumentProgress.Add(progress);
                }

                if (item.UpdatedAt > progress.LastReadAt)
                {
                    progress.PageNumber = item.PageNumber;
                    progress.Percentage = item.Percentage;
                    progress.LastReadAt = item.UpdatedAt;
                }
            }

            // -------------------------
            // 2. APPLY ANNOTATIONS
            // -------------------------
            foreach (var item in request.AnnotationUpdates)
            {
                var existing = await _db.LegalDocumentAnnotations
                    .FirstOrDefaultAsync(a =>
                        a.UserId == userId &&
                        a.ClientId == item.ClientId);

                if (existing == null)
                {
                    _db.LegalDocumentAnnotations.Add(new LegalDocumentAnnotation
                    {
                        UserId = userId,
                        ClientId = item.ClientId,
                        LegalDocumentId = item.LegalDocumentId,
                        Type = item.Type,
                        PageNumber = item.PageNumber,
                        StartCharOffset = item.StartCharOffset,
                        EndCharOffset = item.EndCharOffset,
                        SelectedText = item.SelectedText,
                        Note = item.Note,
                        Color = item.Color,
                        CreatedAt = item.UpdatedAt,
                        UpdatedAt = item.UpdatedAt
                    });
                }
                else if (item.UpdatedAt > existing.UpdatedAt)
                {
                    existing.Note = item.Note;
                    existing.Color = item.Color;
                    existing.UpdatedAt = item.UpdatedAt;
                }
            }

            await _db.SaveChangesAsync();

            // -------------------------
            // 3. SEND SERVER CHANGES
            // -------------------------
            var serverProgress = await _db.LegalDocumentProgress
                .Where(p =>
                    p.UserId == userId &&
                    p.LastReadAt > request.LastSyncAt)
                .Select(p => new SyncProgressItem(
                    p.LegalDocumentId,
                    p.PageNumber,
                    p.Percentage,
                    p.LastReadAt
                ))
                .ToListAsync();

            var serverAnnotations = await _db.LegalDocumentAnnotations
                .Where(a =>
                    a.UserId == userId &&
                    a.UpdatedAt > request.LastSyncAt)
                .Select(a => new SyncAnnotationItem(
                    a.ClientId,
                    a.LegalDocumentId,
                    a.Type,
                    a.PageNumber,
                    a.StartCharOffset,
                    a.EndCharOffset,
                    a.SelectedText,
                    a.Note,
                    a.Color,
                    a.UpdatedAt
                ))
                .ToListAsync();

            return Ok(new SyncResponse(
                now,
                serverProgress,
                serverAnnotations
            ));
        }
    }
}
