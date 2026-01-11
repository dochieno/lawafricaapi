using LawAfrica.API.Models;

namespace LawAfrica.API.Services
{
    public interface IDocumentIndexingService
    {
        Task IndexPdfAsync(LegalDocument document);
    }
}
