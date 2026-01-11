using LawAfrica.API.Data;
using LawAfrica.API.Models;
using LawAfrica.API.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/legal-documents/{documentId}/structure")]
    public class LegalDocumentStructureController : ControllerBase
    {
        private readonly ApplicationDbContext _db;

        public LegalDocumentStructureController(ApplicationDbContext db)
        {
            _db = db;
        }

        // ---------------- ADMIN: ADD NODE ----------------
        [Authorize(Policy = "RequireAdmin")]
        [HttpPost]
        public async Task<IActionResult> AddNode(int documentId, [FromBody] LegalDocumentNodeCreateRequest request)
        {
            if (documentId != request.LegalDocumentId)
                return BadRequest("Document ID mismatch.");

            var node = new LegalDocumentNode
            {
                LegalDocumentId = request.LegalDocumentId,
                ParentId = request.ParentId,
                Title = request.Title,
                Content = request.Content,
                NodeType = request.NodeType,
                Order = request.Order
            };

            _db.LegalDocumentNodes.Add(node);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Node added successfully." });
        }

        // ---------------- PUBLIC: GET STRUCTURE ----------------
        [HttpGet]
        public async Task<IActionResult> GetStructure(int documentId)
        {
            var nodes = await _db.LegalDocumentNodes
                .Where(n => n.LegalDocumentId == documentId && n.ParentId == null)
                .OrderBy(n => n.Order)
                .Include(n => n.Children.OrderBy(c => c.Order))
                .ToListAsync();

            var result = nodes.Select(MapNode).ToList();
            return Ok(result);
        }

        private static LegalDocumentNodeResponse MapNode(LegalDocumentNode node)
        {
            return new LegalDocumentNodeResponse
            {
                Id = node.Id,
                Title = node.Title,
                NodeType = node.NodeType,
                Order = node.Order,
                Content = node.Content,
                Children = node.Children
                    .OrderBy(c => c.Order)
                    .Select(MapNode)
                    .ToList()
            };
        }
    }
}
