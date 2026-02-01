using System.Threading.Channels;

namespace LawAfrica.API.Services.Documents.Indexing
{
    public sealed record LegalDocumentIndexJob(int LegalDocumentId, bool Force);

    public interface ILegalDocumentIndexingQueue
    {
        ValueTask EnqueueAsync(LegalDocumentIndexJob job, CancellationToken ct);
        ValueTask<LegalDocumentIndexJob> DequeueAsync(CancellationToken ct);
    }

    public sealed class LegalDocumentIndexingQueue : ILegalDocumentIndexingQueue
    {
        private readonly Channel<LegalDocumentIndexJob> _channel;

        public LegalDocumentIndexingQueue()
        {
            // Single reader worker, multiple writers (HTTP requests)
            _channel = Channel.CreateUnbounded<LegalDocumentIndexJob>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
        }

        public ValueTask EnqueueAsync(LegalDocumentIndexJob job, CancellationToken ct)
            => _channel.Writer.WriteAsync(job, ct);

        public ValueTask<LegalDocumentIndexJob> DequeueAsync(CancellationToken ct)
            => _channel.Reader.ReadAsync(ct);
    }
}
