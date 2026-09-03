using AsyncCsvProcessor.Application;
using AsyncCsvProcessor.Domain;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AsyncCsvProcessor.Worker.Consumer;

public class JobSubmittedConsumer : IConsumer<JobSubmitted>
{
    private readonly IAsyncCsvProcessorDbContext _db;
    private readonly IEnumerable<IJobFileProcessor> _fileProcessors;
    private readonly ILogger<JobSubmittedConsumer> _logger;

    public JobSubmittedConsumer(
        IAsyncCsvProcessorDbContext db,
        IEnumerable<IJobFileProcessor> fileProcessors,
        ILogger<JobSubmittedConsumer> logger)
    {
        _db = db;
        _fileProcessors = fileProcessors;
        _logger = logger;
    }
    
    public async Task Consume(ConsumeContext<JobSubmitted> context)
    {
        var message = context.Message;
        var job = await _db.Jobs.FindAsync([message.JobId], context.CancellationToken);

        if (job is null)
        {
            _logger.LogWarning("Job {jobId} not found, the message is discarded", message.JobId);
            return;
        }

        await MarkJobAsProcessing(job, context.CancellationToken);
        
        var processor = _fileProcessors.FirstOrDefault(p => p.CanProcess(message.FilePath));
        if (processor is null)
        {
            _logger.LogWarning("No processor registered for file {FilePath}", message.FilePath);
            await MarkJobAsFailed(job, context.CancellationToken);
            return;
        }
        
        var result = await processor.ProcessAsync(message.FilePath, context.CancellationToken);
        
        await CompleteJob(job, result, context.CancellationToken);
    }

    private async Task MarkJobAsProcessing(Job job, CancellationToken ct)
    {
        job.MarkAsProcessing();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Job {JobId} marked as Processing", job.Id);
    }

    private async Task MarkJobAsFailed(Job job, CancellationToken ct)
    {
        job.MarkAsFailed();
        await _db.SaveChangesAsync(ct);
    }

    private async Task CompleteJob(Job job, JobFileProcessingResult result, CancellationToken ct)
    {
        foreach (var error in result.Errors)
            _db.JobRowErrors.Add(new JobRowError(job.Id, error.RowNumber, error.Message));
        
        await UpsertProducts(result.ValidRows, ct);
        
        job.MarkAsCompleted(result.TotalRows, result.ValidRows.Count);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Job {JobId} marked as {Status} ({Processed}/{Total} rows)",
            job.Id, job.Status, result.ValidRows.Count, result.TotalRows);
    }
    
    private async Task UpsertProducts(IReadOnlyList<ProductData> validRows, CancellationToken ct)
    {
        var skus = validRows.Select(data => data.Sku).Distinct().ToList();
        var existingProducts = await _db.Products
            .Where(product => skus.Contains(product.Sku))
            .ToDictionaryAsync(product => product.Sku, ct);
        
        foreach (var productData in validRows)
        {
            if (existingProducts.TryGetValue(productData.Sku, out var product))
            {
                product.UpdateFrom(productData);
            }
            else
            {
                var newProduct = new Product(productData);
                _db.Products.Add(newProduct);
                existingProducts[productData.Sku] = newProduct;
            }
        }
    }
}