using System.Threading.Tasks;

namespace LawAfrica.API.Services.Usage
{
    public interface IUsageEventWriter
    {
        Task LogLegalDocumentAccessAsync(
            int legalDocumentId,
            bool allowed,
            string reason,
            string surface = "ReaderOpen",
            int? userId = null,
            int? institutionId = null);
    }
}
