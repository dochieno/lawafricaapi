using LawAfrica.API.Data;
using LawAfrica.API.Helpers;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using LawAfrica.API.Models.DTOs.LegalDocumentNotes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-document-notes")]
    [Authorize]
    public class LegalDocumentNotesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public LegalDocumentNotesController(ApplicationDbContext db)
        {
            _db = db;
        }


        //1.0 ---------------- CREATE NOTE ----------------
        [HttpPost]
        public async Task<IActionResult> Create(CreateLegalDocumentNoteRequest request)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userId = User.GetUserId();

            var documentExists = await _db.LegalDocuments
                .AnyAsync(d => d.Id == request.LegalDocumentId);

            if (!documentExists)
                return BadRequest("Legal document not found.");

            // ✅ Overlap prevention (only if we have offsets + page)
            if (request.PageNumber.HasValue &&
                request.CharOffsetStart.HasValue &&
                request.CharOffsetEnd.HasValue)
            {
                var start = Math.Min(request.CharOffsetStart.Value, request.CharOffsetEnd.Value);
                var end = Math.Max(request.CharOffsetStart.Value, request.CharOffsetEnd.Value);

                var hasOverlap = await _db.LegalDocumentNotes.AnyAsync(n =>
                    n.UserId == userId &&
                    n.LegalDocumentId == request.LegalDocumentId &&
                    n.PageNumber == request.PageNumber &&
                    n.CharOffsetStart != null &&
                    n.CharOffsetEnd != null &&
                    // overlap check: [start,end) intersects [nStart,nEnd)
                    start < Math.Max(n.CharOffsetStart.Value, n.CharOffsetEnd.Value) &&
                    end > Math.Min(n.CharOffsetStart.Value, n.CharOffsetEnd.Value)
                );

                if (hasOverlap)
                    return Conflict("A highlight already exists in that selected range.");
            }

                var note = new LegalDocumentNote
                {
                    LegalDocumentId = request.LegalDocumentId,
                    UserId = userId,

                    Content = request.Content.Trim(),
                    PageNumber = request.PageNumber,
                    Chapter = request.Chapter,

                    HighlightedText = request.HighlightedText?.Trim(),
                    CharOffsetStart = request.CharOffsetStart,
                    CharOffsetEnd = request.CharOffsetEnd,

                    HighlightColor = string.IsNullOrWhiteSpace(request.HighlightColor)
                        ? "yellow"
                        : request.HighlightColor.Trim().ToLowerInvariant(),

                    CreatedAt = DateTime.UtcNow
                };

                _db.LegalDocumentNotes.Add(note);
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    message = "Note added successfully.",
                    noteId = note.Id
                });
            }


        //2.0 ---------------- GET NOTES FOR DOCUMENT ----------------
        [HttpGet("document/{documentId}")]
        public async Task<IActionResult> GetForDocument(int documentId)
        {
            var userId = User.GetUserId();

            var notes = await _db.LegalDocumentNotes
                .Where(n => n.LegalDocumentId == documentId && n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    n.Id,
                    n.Content,
                    n.PageNumber,
                    n.Chapter,

                    n.HighlightedText,
                    n.CharOffsetStart,
                    n.CharOffsetEnd,
                    n.HighlightColor,

                    n.CreatedAt,
                    n.UpdatedAt
                })
                .ToListAsync();

            return Ok(notes);
        }


        // ---------------- UPDATE NOTE ----------------
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(
            int id,
            UpdateLegalDocumentNoteRequest request)
        {
            var userId = User.GetUserId();

            var note = await _db.LegalDocumentNotes
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (note == null)
                return NotFound("Note not found.");

            note.Content = request.Content.Trim();
            note.PageNumber = request.PageNumber;
            note.Chapter = request.Chapter;
            note.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.HighlightColor))
            {
                note.HighlightColor = request.HighlightColor.Trim().ToLowerInvariant();
            }


            await _db.SaveChangesAsync();

            return Ok(new { message = "Note updated successfully." });
        }

        // ---------------- DELETE NOTE ----------------
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = User.GetUserId();

            var note = await _db.LegalDocumentNotes
                .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);

            if (note == null)
                return NotFound("Note not found.");

            _db.LegalDocumentNotes.Remove(note);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Note deleted successfully." });
        }
    }
}
