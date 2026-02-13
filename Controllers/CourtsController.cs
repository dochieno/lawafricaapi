using LawAfrica.API.Models.LawReports.DTOs;
using LawAfrica.API.Services.LawReports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers
{
    [ApiController]
    [Route("api/courts")]
    [Authorize(Roles = "Admin")]
    public class CourtsController : ControllerBase
    {
        private readonly CourtsService _service;

        public CourtsController(CourtsService service)
        {
            _service = service;
        }

        // -------------------------
        // ADMIN LIST
        // GET /api/courts?countryId=1&q=high&includeInactive=true
        // -------------------------
        [HttpGet]
        public async Task<ActionResult<List<CourtDto>>> AdminList(
            [FromQuery] int? countryId = null,
            [FromQuery] string? q = null,
            [FromQuery] bool includeInactive = false,
            CancellationToken ct = default)
        {
            try
            {
                var list = await _service.AdminListAsync(countryId, q, includeInactive, ct);
                return Ok(list);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to load courts.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // GET single
        // GET /api/courts/12
        // -------------------------
        [HttpGet("{id:int}")]
        public async Task<ActionResult<CourtDto>> Get(int id, CancellationToken ct)
        {
            var item = await _service.GetAsync(id, ct);
            if (item == null) return NotFound(new { message = "Court not found." });
            return Ok(item);
        }

        // -------------------------
        // CREATE
        // POST /api/courts
        // -------------------------
        [HttpPost]
        public async Task<ActionResult<CourtDto>> Create([FromBody] CourtUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            try
            {
                var created = await _service.CreateAsync(dto, ct);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (InvalidOperationException ex)
            {
                // duplicates/validation -> 400 (or 409 when you prefer)
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to create court.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // UPDATE
        // PUT /api/courts/12
        // -------------------------
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CourtUpsertDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return ValidationProblem(ModelState);

            try
            {
                await _service.UpdateAsync(id, dto, ct);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Court not found." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to update court.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }

        // -------------------------
        // DELETE
        // DELETE /api/courts/12
        // -------------------------
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            try
            {
                await _service.DeleteAsync(id, ct);
                return NoContent();
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new { message = "Court not found." });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Failed to delete court.",
                    detail = ex.Message,
                    type = ex.GetType().Name
                });
            }
        }
    }
}
