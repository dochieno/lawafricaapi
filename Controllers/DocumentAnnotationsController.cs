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
    [Route("api/documents/{documentId:int}/annotations")]
    [Authorize]
    public class DocumentAnnotationsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public DocumentAnnotationsController(ApplicationDbContext db)
        {
            _db = db;
        }

        // --------------------------------------------------
        // GET: /api/documents/{documentId}/annotations
        // --------------------------------------------------
        [HttpGet]
        public async Task<IActionResult> GetAll(int documentId)
        {
            var userId = User.GetUserId();

            var items = await _db.LegalDocumentAnnotations
                .AsNoTracking()
                .Where(x =>
                    x.UserId == userId &&
                    x.LegalDocumentId == documentId)
                .OrderBy(x => x.PageNumber)
                .ThenBy(x => x.CreatedAt)
                .ToListAsync();

            var response = items.Select(x => new AnnotationResponse(
                x.Id,
                x.Type,
                x.PageNumber,
                x.StartCharOffset,
                x.EndCharOffset,
                x.SelectedText,
                x.Note,
                x.Color,
                x.CreatedAt
            ));

            return Ok(response);
        }

        // --------------------------------------------------
        // POST: /api/documents/{documentId}/annotations
        // --------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> Create(
            int documentId,
            CreateAnnotationRequest request)
        {
            var userId = User.GetUserId();

            var annotation = new LegalDocumentAnnotation
            {
                UserId = userId,
                LegalDocumentId = documentId,
                Type = request.Type,
                PageNumber = request.PageNumber,
                StartCharOffset = request.StartCharOffset,
                EndCharOffset = request.EndCharOffset,
                SelectedText = request.SelectedText,
                Note = request.Note,
                Color = request.Color
            };

            _db.LegalDocumentAnnotations.Add(annotation);
            await _db.SaveChangesAsync();

            return Ok(new AnnotationResponse(
                annotation.Id,
                annotation.Type,
                annotation.PageNumber,
                annotation.StartCharOffset,
                annotation.EndCharOffset,
                annotation.SelectedText,
                annotation.Note,
                annotation.Color,
                annotation.CreatedAt
            ));
        }

        // --------------------------------------------------
        // DELETE: /api/documents/{documentId}/annotations/{id}
        // --------------------------------------------------
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int documentId, int id)
        {
            var userId = User.GetUserId();

            var item = await _db.LegalDocumentAnnotations
                .FirstOrDefaultAsync(x =>
                    x.Id == id &&
                    x.UserId == userId &&
                    x.LegalDocumentId == documentId);

            if (item == null)
                return NotFound();

            _db.LegalDocumentAnnotations.Remove(item);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
