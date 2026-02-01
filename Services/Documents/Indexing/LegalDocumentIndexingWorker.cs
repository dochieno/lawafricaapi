using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LawAfrica.API.Services.Documents.Indexing
{
    public sealed class LegalDocumentIndexingWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILegalDocumentIndexingQueue _queue;
        private readonly ILogger<LegalDocumentIndexingWorker> _logger;

        public LegalDocumentIndexingWorker(
            IServiceProvider sp,
            ILegalDocumentIndexingQueue queue,
            ILogger<LegalDocumentIndexingWorker> logger)
        {
            _sp = sp;
            _queue = queue;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("LegalDocumentIndexingWorker started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                LegalDocumentIndexJob job;

                try
                {
                    job = await _queue.DequeueAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Indexing worker dequeue failed.");
                    continue;
                }

                try
                {
                    using var scope = _sp.CreateScope();
                    var indexer = scope.ServiceProvider.GetRequiredService<ILegalDocumentTextIndexer>();

                    _logger.LogInformation("Indexing doc {DocId} (force={Force})", job.LegalDocumentId, job.Force);

                    var result = await indexer.IndexAsync(job.LegalDocumentId, job.Force, stoppingToken);

                    if (result.Skipped)
                    {
                        _logger.LogWarning("Index skipped for doc {DocId}: {Reason}", job.LegalDocumentId, result.SkipReason);
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Index done for doc {DocId}: pages {Indexed}/{Total}, emptyTextPages={Empty}",
                            result.LegalDocumentId, result.PagesIndexed, result.PagesTotal, result.PagesEmptyText);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Index failed for doc {DocId}", job.LegalDocumentId);
                }
            }

            _logger.LogInformation("LegalDocumentIndexingWorker stopped.");
        }
    }
}
