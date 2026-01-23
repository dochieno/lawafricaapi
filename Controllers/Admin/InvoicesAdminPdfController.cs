using LawAfrica.API.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LawAfrica.API.Controllers.Admin
{
    [ApiController]
    [Route("api/admin/invoices")]
    [Authorize(Roles = "Admin")]
    public class InvoicesAdminPdfController : ControllerBase
    {
        private readonly InvoicePdfService _pdf;

        public InvoicesAdminPdfController(InvoicePdfService pdf)
        {
            _pdf = pdf;
        }

        /// <summary>
        /// Download invoice PDF (Admin).
        /// GET /api/admin/invoices/{id}/pdf
        /// </summary>
        [HttpGet("{id:int}/pdf")]
        public async Task<IActionResult> GetPdf(int id, CancellationToken ct)
        {
            byte[] pdfBytes;

            try
            {
                pdfBytes = await _pdf.GenerateInvoicePdfAsync(id, ct);
            }
            catch (InvalidOperationException ex)
            {
                // e.g. Invoice not found
                return NotFound(ex.Message);
            }

            // We generate the PDF from the invoice, but we still want a stable filename.
            // If you want exact InvoiceNumber in filename, you can optionally add a small helper
            // in the service to return (bytes, invoiceNumber). For now, keep it safe.
            var fileName = $"INV-{id}.pdf";

            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}
